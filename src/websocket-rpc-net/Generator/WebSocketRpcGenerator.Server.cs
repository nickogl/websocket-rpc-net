using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	private static ServerModel? ExtractServerModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
	{
		return context.SemanticModel.GetDeclaredSymbol(context.Node) is INamedTypeSymbol symbol
			? ExtractServerModel(symbol, cancellationToken, out _)
			: null;
	}

	private static ServerModel? ExtractServerModel(INamedTypeSymbol symbol, CancellationToken cancellationToken, out ServerMetadata? metadata)
	{
		metadata = ExtractServerMetadata(symbol);
		if (metadata == null || cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		var classModel = ExtractClassModel(symbol, firstParameterType: metadata.ClientType);
		if (classModel == null || cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		return new ServerModel()
		{
			Class = classModel.Value,
			ClientClassNamespace = GetFullyQualifiedNamespace(metadata.ClientType.ContainingNamespace),
			ClientClassName = metadata.ClientType.Name,
			Serializer = CreateSerializerModel(classModel.Value, metadata, isClient: false),
		};
	}

	private static ServerMetadata? ExtractServerMetadata(INamedTypeSymbol symbol)
	{
		foreach (var attribute in symbol.GetAttributes())
		{
			if (attribute.AttributeClass?.Name != "WebSocketRpcServerAttribute")
			{
				continue;
			}
			// Abort source generation in case of invalid attribute usage
			if (attribute.AttributeClass.TypeArguments.Length != 1 ||
				attribute.AttributeClass.TypeArguments[0] is not INamedTypeSymbol clientSymbolCandidate ||
				attribute.ConstructorArguments.Length != 1 ||
				attribute.ConstructorArguments[0].Value is not int serializationMode ||
				serializationMode < 0 || serializationMode > 1)
			{
				return null;
			}

			foreach (var clientAttribute in clientSymbolCandidate.GetAttributes())
			{
				if (clientAttribute.AttributeClass?.Name == "WebSocketRpcClientAttribute")
				{
					return new() { ClientType = clientSymbolCandidate, UsesGenericSerialization = serializationMode == 0 };
				}
			}
		}

		return null;
	}

	private static void GenerateServerClass(SourceProductionContext context, ServerModel serverModel)
	{
		if (serverModel.Serializer != null)
		{
			GenerateSerializerInterface(context, serverModel.Serializer.Value);
		}

		var serverClass = new StringBuilder(@$"
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Threading;

namespace {serverModel.Class.Namespace};

partial class {serverModel.Class.Name}
{{
	/// <summary>Time after which clients are disconnected if they fail to acknowledge ping frames.</summary>
	/// <remarks>Setting this to null (default) disables quick detection of disconnects.</remarks>
	private TimeSpan? _connectionTimeout = null;

	/// <summary>Array pool to use for allocating buffers.</summary>
	private ArrayPool<byte> _allocator = ArrayPool<byte>.Shared;

	/// <summary>Size of the message buffer in bytes. Defaults to 8 KiB.</summary>
	/// <remarks>
	/// Choose one that the vast majority of messages will fit into.
	/// If a message does not fit, the buffer grows exponentially until <see cref=""_maximumMessageSize""/> is reached.
	/// If you are unsure, choose a generous value first and then consult the recorded metrics to refine it.
	/// </remarks>
	private int _messageBufferSize = 1024 * 8;

	/// <summary>Maximum size of messages. Defaults to 64 KiB.</summary>
	/// <remarks>
	/// Choose one that all legit messages will fit into.
	/// If you are unsure, choose a generous value first and then consult the recorded metrics to refine it.
	/// </remarks>
	private int _maximumMessageSize = 1024 * 64;

	/// <summary>Maximum size of parameters. Defaults to 4 KiB.</summary>
	/// <remarks>
	/// Choose one that all parameters of legit messages will fit into.
	/// If you are unsure, choose a generous value first and then consult the recorded metrics to refine it.
	/// </remarks>
	private int _maximumParameterSize = 1024 * 4;
");
		if (serverModel.Serializer != null)
		{
			serverClass.AppendLine(@$"
	/// <summary>Serializer to serialize and deserialize RPC parameters.</summary>
	/// <remarks>Initialize this field in the constructor of your class.</remarks>
	private {GetFullyQualifiedType(serverModel.Serializer.Value.InterfaceNamespace, serverModel.Serializer.Value.InterfaceName)} _serializer;");
		}
		serverClass.Append(@$"
	public async Task ProcessAsync({GetFullyQualifiedType(serverModel.ClientClassNamespace, serverModel.ClientClassName)} client, CancellationToken cancellationToken)
	{{
		if (cancellationToken.IsCancellationRequested)
		{{
			return;
		}}
		var __cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		if (_connectionTimeout != null)
		{{
			__cts.CancelAfter(_connectionTimeout.Value);
		}}
		cancellationToken = __cts.Token;
		client.Disconnected = __cts.Token;

		try
		{{
			int __read = 0;
			int __processed = 0;
			while (!cancellationToken.IsCancellationRequested && client.WebSocket.State == WebSocketState.Open)
			{{
				ValueWebSocketReceiveResult __result = default;
				var __buffer = _allocator.Rent(_messageBufferSize);
				try
				{{
					do
					{{
						{GenerateReadInt32("methodKey", "Incomplete method key", 6)}

						switch (methodKey)
						{{
							case 0:
								if (__cts == null) throw new InvalidDataException(""Did not expect a pong frame, as the server is not configured to send ping frames"");
								__cts.TryReset(); // restart timeout
								break;
						");
		foreach (var method in serverModel.Class.Methods)
		{
			serverClass.AppendLine(@$"
							//
							// {method.Name}({GenerateParameterList(method.Parameters, types: false)})
							//
							case {method.Key}:");
			foreach (var param in method.Parameters)
			{
				var lengthVariable = $"__{param.Name}Length";
				var deserialize = GenerateDeserializeCall(param.Type, serverModel.Serializer, innerExpression: "{0}");
				serverClass.Append($@"{Indent(8)}{GenerateReadInt32(lengthVariable, "Incomplete parameter length", 8)}
								if ({lengthVariable} > _maximumParameterSize) throw new InvalidDataException(""Parameter exceeds maximum length: {param.Name}"");
								{GenerateReadExactly(lengthVariable, $"var {param.Name} = _serializer.{deserialize}", "Incomplete parameter data", 8)}");
			}
			serverClass.AppendLine(@$"
								await {method.Name}(client, {GenerateParameterList(method.Parameters, types: false)});
								break;");
		}

		serverClass.AppendLine(@$"
							default:
								throw new InvalidDataException($""Invalid method key: {{methodKey}}"");
						}}
					}} while (!__result.EndOfMessage);
				}}
				finally
				{{
					_allocator.Return(__buffer);
				}}
			}}
		}}
		finally
		{{
			__cts.Cancel();
			__cts.Dispose();
		}}
	}}
}}");
		context.AddSource($"{serverModel.Class.Name}.g.cs", serverClass.ToString());

		byte[] newBuffer = new byte[1];
		byte[] buffer = new byte[1];
		buffer.CopyTo(newBuffer.AsSpan());
	}

	private sealed class ServerMetadata : MetadataBase
	{
		public required INamedTypeSymbol ClientType { get; init; }
	}
}
