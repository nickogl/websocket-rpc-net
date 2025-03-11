using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class RpcServerGenerator
{
	internal static ServerModel? ExtractServerModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
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
			if (!IsRpcServerAttribute(attribute))
			{
				continue;
			}
			// Abort source generation in case of invalid attribute usage
			if (attribute.AttributeClass!.TypeArguments.Length != 1 ||
				attribute.AttributeClass.TypeArguments[0] is not INamedTypeSymbol clientSymbolCandidate ||
				attribute.ConstructorArguments.Length != 1 ||
				attribute.ConstructorArguments[0].Value is not int serializationMode ||
				serializationMode < 0 || serializationMode > 1)
			{
				return null;
			}

			foreach (var clientAttribute in clientSymbolCandidate.GetAttributes())
			{
				if (IsRpcClientAttribute(clientAttribute))
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
#nullable enable

using Nickogl.WebSockets.Rpc;
using Nickogl.WebSockets.Rpc.Internal;
using Nickogl.WebSockets.Rpc.Serialization;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading;

namespace {serverModel.Class.Namespace};

{serverModel.Class.Visibility} abstract class {serverModel.Class.Name}Base : RpcServerBase<{GetFullyQualifiedType(serverModel.ClientClassNamespace, serverModel.ClientClassName)}>
{{");
		if (serverModel.Serializer != null)
		{
			serverClass.AppendLine(@$"
	/// <summary>Serializer to deserialize parameters from RPCs received from clients.</summary>
	protected abstract {GetFullyQualifiedType(serverModel.Serializer.Value.InterfaceNamespace, serverModel.Serializer.Value.InterfaceName)} Serializer {{ get; }}");
		}
		serverClass.Append(@$"
}}

{serverModel.Class.Visibility} partial class {serverModel.Class.Name} : {serverModel.Class.Name}Base
{{
	protected override ValueTask DispatchAsync({GetFullyQualifiedType(serverModel.ClientClassNamespace, serverModel.ClientClassName)} __client, int __methodKey, IRpcMessageReader __messageReader)
	{{
		switch (__methodKey)
		{{");
		foreach (var method in serverModel.Class.Methods)
		{
			serverClass.Append(@$"
			//
			// {method.Name}({GetParameterList(method.Parameters, types: false)})
			//
			case {method.Key}:
			{{");
			var hasDisposableParams = method.Parameters.Any(param => param.Type.IsDisposable);
			if (hasDisposableParams)
			{
				serverClass.Append(@$"
				static async ValueTask Dispatch{method.Name}({GetFullyQualifiedType(serverModel.ClientClassNamespace, serverModel.ClientClassName)} __client, IRpcMessageReader __messageReader)
				{{");
			}
			foreach (var param in method.Parameters)
			{
				var deserialize = serverModel.Serializer!.Value.IsGeneric
					? $"Serializer.Deserialize<{param.Type.Name}>"
					: $"Serializer.Deserialize{param.Type.EscapedName}";
				serverClass.Append(@$"
				__messageReader.BeginReadParameter();
				{(param.Type.IsDisposable ? "using " : string.Empty)}var {param.Name} = {deserialize}(__messageReader.ParameterReader);
				__messageReader.EndReadParameter();");
			}
			var paramPrefix = method.Parameters.Length > 0 ? ", " : string.Empty;
			if (hasDisposableParams)
			{
				serverClass.AppendLine($@"
				await {method.Name}(__client{paramPrefix}{GetParameterList(method.Parameters, types: false)}).ConfigureAwait(false);
				}}
				return Dispatch{method.Name}(__client, __messageReader);");
			}
			else
			{
				serverClass.AppendLine(@$"
				return {method.Name}(__client{paramPrefix}{GetParameterList(method.Parameters, types: false)});
			}}");
			}
		}

		serverClass.AppendLine(@$"
			default:
				throw new InvalidDataException($""Invalid method key: {{__methodKey}}"");
		}}
	}}
}}");
		context.AddSource($"{serverModel.Class.Name}.g.cs", serverClass.ToString());
	}

	private sealed class ServerMetadata : MetadataBase
	{
		public required INamedTypeSymbol ClientType { get; init; }
	}
}
