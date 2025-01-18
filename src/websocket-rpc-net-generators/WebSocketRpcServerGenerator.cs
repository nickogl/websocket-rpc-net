using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generators;

[Generator]
public class WebSocketRpcServerGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var serverBlueprints =
			context.SyntaxProvider
				.CreateSyntaxProvider(IsCandidate, WebSocketRpcServerBlueprintParser.ParseBlueprint)
				.Where(blueprint => blueprint is not null);
		context.RegisterSourceOutput(
			serverBlueprints,
			(context, blueprint) => Generate(context, blueprint!.Value));
	}

	private static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken)
	{
		return node is InterfaceDeclarationSyntax interfaceNode &&
			interfaceNode.AttributeLists.Count > 0 &&
			interfaceNode.BaseList != null &&
			interfaceNode.BaseList.Types.Count > 0;
	}

	private static void Generate(SourceProductionContext context, WebSocketRpcServerBlueprint blueprint)
	{
		var serverClassName = $"{blueprint.ServerName.TrimStart('I')}Base";
		var clientClassName = $"{blueprint.ClientName.TrimStart('I')}Base";
		var fqClientInterfaceName =
			blueprint.ServerNamespace == blueprint.ClientNamespace
				? blueprint.ClientName
				: $"{blueprint.ClientNamespace}.{blueprint.ClientName}";

		var stringBuilder = new StringBuilder(@$"using System.Buffers.Binary;
using System.Net.WebSockets;
using Nickogl.WebSockets.Rpc;

namespace {blueprint.ServerNamespace}
{{
	public abstract class {serverClassName} : WebSocketRpcServerBase<{fqClientInterfaceName}>, {blueprint.ServerName}
	{{");
		// Serialization methods to implement
		if (blueprint.Metadata.SerializationMode == WebSocketRpcServerBlueprint.ParameterSerializationMode.Generic)
		{
			stringBuilder.AppendLine(@$"
		/// <summary>Serialize an RPC method parameter of type <typeparamref name=""T""/>.</summary>
		/// <remarks>This has to write </remarks>
		protected abstract ReadOnlySpan<byte> Serialize<T>(T parameter);

		/// <summary>Deserialize an RPC method parameter of type <typeparamref name=""T""/> from <paramref name=""data""/>.</summary>
		protected abstract T Deserialize<T>(ReadOnlySpan<byte> data);");
		}
		else if (blueprint.Metadata.SerializationMode == WebSocketRpcServerBlueprint.ParameterSerializationMode.Specialized)
		{
			var uniqueParameterTypes =
				blueprint.ServerMethods
					.SelectMany(method => method.Parameters)
					.Union(blueprint.ClientMethods.SelectMany(method => method.Parameters))
					.Distinct(new ParameterTypeEqualityComparer())
					.Select(param => (param.Type, param.EscapedType));
			foreach (var (type, escapedType) in uniqueParameterTypes)
			{
				stringBuilder.AppendLine(@$"
		/// <summary>Serialize an RPC method parameter of type <see cref=""{type}""/>.</summary>
		protected abstract ReadOnlySpan<byte> Serialize{escapedType}({type} parameter);

		/// <summary>Deserialize an RPC method parameter of type <see cref=""{type}""/> from <paramref name=""data""/>.</summary>
		protected abstract {type} Deserialize{escapedType}(ReadOnlySpan<byte> data);");
			}
		}
		else
		{
			throw new NotSupportedException($"Generation code for serialization mode '{blueprint.Metadata.SerializationMode}' is not supported");
		}

		// RPC methods to implement
		foreach (var method in blueprint.ServerMethods)
		{
			var paramList = string.Join(", ", method.Parameters.Select(param => $"{param.Type} {param.Name}"));
			stringBuilder.AppendLine(@$"
		/// <summary>RPC method key: {method.Metadata.Key}</summary>
		public abstract {MethodReturnTypeToString(method.ReturnType)} {method.Name}({fqClientInterfaceName} client, {paramList});");
		}

		// Message loop and RPC dispatch
		stringBuilder.AppendLine(@$"
		public async Task ProcessAsync({fqClientInterfaceName} client, CancellationToken cancellationToken)
		{{
			if (cancellationToken.IsCancellationRequested)
				return;
			var cts = ConnectionTimeout == null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			cts?.CancelAfter(ConnectionTimeout!.Value);
			cancellationToken = cts?.Token ?? cancellationToken;

			try
			{{
				await OnConnectAsync(client);

				int read = 0;
				int processed = 0;
				while (!cancellationToken.IsCancellationRequested && client.WebSocket.State != WebSocketState.Open)
				{{
					ValueWebSocketReceiveResult result;
					var buffer = Allocator.Rent(MessageBufferSize);
					try
					{{
						do
						{{
							{GenerateReadInt32("methodKey", "Incomplete method key", 7)}");

		// RPC dispatch
		stringBuilder.Append(@$"
							switch (methodKey)
							{{");
		foreach (var method in blueprint.ServerMethods.OrderBy(method => method.Metadata.Key))
		{
			var isTask =
				method.ReturnType == WebSocketRpcServerBlueprint.MethodReturnType.ValueTask ||
				method.ReturnType == WebSocketRpcServerBlueprint.MethodReturnType.Task;
			stringBuilder.AppendLine(@$"
								// {method.Name}
								case {method.Metadata.Key}:");
			foreach (var param in method.Parameters)
			{
				var deserializationCall = blueprint.Metadata.SerializationMode switch
				{
					WebSocketRpcServerBlueprint.ParameterSerializationMode.Generic => $"Deserialize<{param.Type}>",
					WebSocketRpcServerBlueprint.ParameterSerializationMode.Specialized => $"Deserialize{param.EscapedType}",
					_ => throw new NotSupportedException($"Deserialization for mode '{blueprint.Metadata.SerializationMode}' is not yet implemented")
				};
				var lengthVariable = $"{param.Name}Length";
				stringBuilder.Append($@"{Indent(9)}{GenerateReadInt32(lengthVariable, "Incomplete parameter length", 9)}
									if ({lengthVariable} > MaximumParameterSize) throw new WebSocketRpcMessageException(""Parameter exceeds maximum length: {param.Name}"");
									{GenerateReadExactly(lengthVariable, $"var {param.Name}Buffer = {{0}}", "Incomplete parameter data", 9)}
									var {param.Name} = {deserializationCall}({param.Name}Buffer);");
			}
			stringBuilder.AppendLine(@$"
									{(isTask ? "await " : "")}{method.Name}(client, {string.Join(", ", method.Parameters.Select(param => param.Name))});
									break;");
		}
		stringBuilder.AppendLine(@$"
								default:
									throw new WebSocketRpcMessageException($""Invalid method key: {{methodKey}}"");
							}}
						}} while (!result.EndOfMessage);
					}}
					finally
					{{
						Allocator.Return(buffer);
					}}
				}}
			}}
			finally
			{{
				cts?.Dispose();

				await OnDisconnectAsync(client);
			}}
		}}");

		// Client implementation
		stringBuilder.Append(@$"
		protected abstract class {clientClassName} : {fqClientInterfaceName}
		{{
			private readonly {serverClassName} _server;

			public {clientClassName}({serverClassName} server)
			{{
				_server = server;
			}}");

		stringBuilder.AppendLine(@$"
		}}
	}}
}}");

		context.AddSource($"{serverClassName}.g.cs", SourceText.From(stringBuilder.ToString(), Encoding.UTF8));
	}

	private static string GenerateReadExactly(string countExpression, string assignmentFormat, string tooFewBytesErrorMessage, int nestingLevel)
	{
		if (nestingLevel < 1)
		{
			throw new ArgumentException("Nesting level must be greater than or equal to 1");
		}

		return @$"while (read < (processed + {countExpression}))
			{{
				if (read == buffer.Length)
				{{
					int newLength = buffer.Length * 2;
					if (newLength > MaximumMessageSize) throw new WebSocketRpcMessageException(""Message exceeds maximum size"");
					Allocator.Return(buffer); buffer = Allocator.Rent(newLength);
				}}
				var destination = new Memory<byte>(buffer, read);
				result = await client.WebSocket.ReceiveAsync(destination, cancellationToken);
				if (result.MessageType == WebSocketMessageType.Close) return;
				else if (result.MessageType != WebSocketMessageType.Binary) throw new WebSocketRpcMessageException($""Invalid message type: {{result.MessageType}}"");
				read += result.Count;
				if (result.EndOfMessage && read < (processed + {countExpression})) throw new WebSocketRpcMessageException(""{tooFewBytesErrorMessage}"");
			}}
			{string.Format(assignmentFormat, $"buffer.AsSpan(processed, {countExpression})")};
			processed += {countExpression};".Replace("\n\t\t\t", $"\n{Indent(nestingLevel)}");
	}

	private static string GenerateReadInt32(string intoVariable, string tooFewBytesErrorMessage, int nestingLevel)
	{
		return GenerateReadExactly("sizeof(int)", $"int {intoVariable} = BinaryPrimitives.ReadInt32LittleEndian({{0}})", tooFewBytesErrorMessage, nestingLevel);
	}

	private static string Indent(int nestingLevel)
	{
		return new string('\t', nestingLevel);
	}

	private static string MethodReturnTypeToString(WebSocketRpcServerBlueprint.MethodReturnType type)
	{
		return type switch
		{
			WebSocketRpcServerBlueprint.MethodReturnType.Void => "void",
			WebSocketRpcServerBlueprint.MethodReturnType.ValueTask => "ValueTask",
			WebSocketRpcServerBlueprint.MethodReturnType.Task => "Task",
			_ => throw new NotSupportedException($"Converting method return type '{type}' to a string is not supported")
		};
	}

	private sealed class ParameterTypeEqualityComparer : EqualityComparer<WebSocketRpcServerBlueprint.Parameter>
	{
		public override bool Equals(WebSocketRpcServerBlueprint.Parameter x, WebSocketRpcServerBlueprint.Parameter y)
		{
			return x.Type.Equals(y.Type, StringComparison.Ordinal);
		}

		public override int GetHashCode(WebSocketRpcServerBlueprint.Parameter obj)
		{
			return obj.Type.GetHashCode();
		}
	}
}
