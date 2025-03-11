using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class RpcServerGenerator
{
	internal static ClientModel? ExtractClientModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
	{
		return context.SemanticModel.GetDeclaredSymbol(context.Node) is INamedTypeSymbol symbol
			? ExtractClientModel(symbol, cancellationToken, out _)
			: null;
	}

	private static ClientModel? ExtractClientModel(INamedTypeSymbol symbol, CancellationToken cancellationToken, out ClientMetadata? metadata)
	{
		metadata = ExtractClientMetadata(symbol);
		if (metadata == null || cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		var classModel = ExtractClassModel(symbol);
		if (classModel == null || cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		return new ClientModel()
		{
			Class = classModel.Value,
			Serializer = CreateSerializerModel(classModel.Value, metadata, isClient: true),
		};
	}

	private static ClientMetadata? ExtractClientMetadata(INamedTypeSymbol symbol)
	{
		foreach (var attribute in symbol.GetAttributes())
		{
			if (!IsRpcClientAttribute(attribute))
			{
				continue;
			}
			// Abort source generation in case of invalid attribute usage
			if (attribute.ConstructorArguments.Length != 1 ||
				attribute.ConstructorArguments[0].Value is not int serializationMode ||
				serializationMode < 0 || serializationMode > 1)
			{
				return null;
			}

			return new() { UsesGenericSerialization = serializationMode == 0 };
		}

		return null;
	}

	private static void GenerateClientClass(SourceProductionContext context, ClientModel clientModel)
	{
		if (clientModel.Serializer != null)
		{
			GenerateSerializerInterface(context, clientModel.Serializer.Value);
		}

		var serializerClassName = clientModel.Serializer != null ? GetFullyQualifiedType(clientModel.Serializer.Value.InterfaceNamespace, clientModel.Serializer.Value.InterfaceName) : null;
		var clientClass = new StringBuilder(@$"
#nullable enable

using Nickogl.WebSockets.Rpc;
using Nickogl.WebSockets.Rpc.Internal;
using Nickogl.WebSockets.Rpc.Serialization;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading;

namespace {clientModel.Class.Namespace};

{clientModel.Class.Visibility} abstract class {clientModel.Class.Name}Base : RpcClientBase
{{");
		if (clientModel.Serializer != null)
		{
			clientClass.AppendLine(@$"
	/// <summary>Serializer to serialize and deserialize RPC parameters.</summary>
	protected abstract {serializerClassName} Serializer {{ get; }}");
		}
		clientClass.Append(@$"
}}

{clientModel.Class.Visibility} partial class {clientModel.Class.Name} : {clientModel.Class.Name}Base
{{");
		foreach (var method in clientModel.Class.Methods)
		{
			var paramPrefix = method.Parameters.Length > 0 ? ", " : string.Empty;
			clientClass.Append(@$"
	/// <summary>
	/// Add a call to the '{method.Name}' procedure to the provided message writer.
	/// </summary>
	/// <remarks>
	/// <para>Use this method if you want to create RPC batches.</para>
	/// <para>This method is thread-safe for independent <see cref=""IRpcMessageWriter""/> instances.</para>
	/// </remarks>
	public void {method.Name}(IRpcMessageWriter __messageWriter{paramPrefix}{GetParameterList(method.Parameters)})
	{{
		__messageWriter.WriteMethodKey({method.Key});");
			foreach (var param in method.Parameters)
			{
				var serialize = clientModel.Serializer!.Value.IsGeneric
					? $"Serializer.Serialize<{param.Type.Name}>"
					: $"Serializer.Serialize{param.Type.EscapedName}";
				clientClass.Append(@$"
		__messageWriter.BeginWriteParameter();
		{serialize}(__messageWriter.ParameterWriter, {param.Name});
		__messageWriter.EndWriteParameter();");
			}
			clientClass.Append(@$"
	}}

	/// <summary>
	/// Call the '{method.Name}' procedure on the client.
	/// </summary>
	/// <remarks>This method is not thread-safe.</remarks>
	/// <exception cref=""OperationCanceledException"">Client disconnected or timed out during this operation.</exception>
	/// <exception cref=""WebSocketException"">Operation on the client's websocket failed.</exception>
	public partial async ValueTask {method.Name}({GetParameterList(method.Parameters)})
	{{
		var __messageWriter = GetMessageWriter();
		try
		{{
			{method.Name}(__messageWriter, {GetParameterList(method.Parameters, types: false)});
			await FlushAsync(__messageWriter);
		}}
		finally
		{{
			ReturnMessageWriter(__messageWriter);
		}}
	}}");
		}
		clientClass.Append("}");
		context.AddSource($"{clientModel.Class.Name}.g.cs", clientClass.ToString());
	}

	private sealed class ClientMetadata : MetadataBase
	{
	}
}
