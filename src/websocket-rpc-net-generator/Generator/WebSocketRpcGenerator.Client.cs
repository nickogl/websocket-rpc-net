using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
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
			if (attribute.AttributeClass?.Name != "WebSocketRpcClientAttribute")
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

		var serializerName = clientModel.Serializer != null ? GetFullyQualifiedType(clientModel.Serializer.Value.InterfaceNamespace, clientModel.Serializer.Value.InterfaceName) : null;
		var clientClass = new StringBuilder(@$"
#nullable enable

using Nickogl.WebSockets.Rpc;
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

public abstract class {clientModel.Class.Name}Base
{{
}}

/// <inheritdoc />
/// <remarks>The auto-generated portion of this class is not thread-safe and should not be used from multiple threads concurrently.</remarks>
partial class {clientModel.Class.Name} : {clientModel.Class.Name}Base
{{
	private WebSocket? _webSocket;

	/// <summary>Get or set the underlying websocket of the client.</summary>
	public WebSocket WebSocket
	{{
		// This is a bit awkward to use, but it allows instantiating clients with a dependency injection framework
		get {{ Debug.Assert(_webSocket != null, ""Must set the websocket right after constructing the client""); return _webSocket; }}
		set {{ _webSocket = value; }}
	}}

	private CancellationToken? _disconnected;

	/// <summary>Get or set the cancellation token triggered after the client disconnected.</summary>
	public CancellationToken Disconnected
	{{
		get {{ Debug.Assert(_disconnected != null); return _disconnected.Value; }}
		set {{ _disconnected = value; }}
	}}

	/// <summary>Array pool to use for allocating buffers.</summary>
	private ArrayPool<byte> _allocator = ArrayPool<byte>.Shared;

	/// <summary>Size of the message buffer in bytes. Defaults to 8 KiB.</summary>
	/// <remarks>
	/// Choose one that the vast majority of standalone messages will fit into.
	/// If a message does not fit, the buffer grows exponentially until <see cref=""_maximumBufferSize""/> is reached.
	/// If you are unsure, choose a generous value first and then consult the recorded metrics to refine it.
	/// </remarks>
	private int _messageBufferSize = 1024 * 8;

	/// <summary>Size of the batch buffer in bytes. Defaults to 32 KiB.</summary>
	/// <remarks>
	/// Choose one that the vast majority of batches will fit into.
	/// If a batch does not fit, the buffer grows exponentially until <see cref=""_maximumBufferSize""/> is reached.
	/// If you are unsure, choose a generous value first and then consult the recorded metrics to refine it.
	/// </remarks>
	private int _batchBufferSize = 1024 * 32;

	/// <summary>Maximum size of buffers. Defaults to 16 MiB and only serves as a safeguard against infinite loop bugs in the code while creating batches.</summary>
	private int _maximumBufferSize = 1024 * 1024 * 16;
");
		if (clientModel.Serializer != null)
		{
			clientClass.AppendLine(@$"
	/// <summary>Serializer to serialize and deserialize RPC parameters.</summary>
	/// <remarks>Initialize this field in the constructor of your class.</remarks>
	private {serializerName} _serializer;");
		}
		foreach (var method in clientModel.Class.Methods)
		{
			clientClass.Append(@$"
	/// <summary>
	/// Call the '{method.Name}' procedure on the client.
	/// </summary>
	/// <exception cref=""OperationCanceledException"">Client disconnected or timed out during this operation.</exception>
	/// <exception cref=""WebSocketException"">Operation on the client's websocket failed.</exception>
	public partial async ValueTask {method.Name}({GetParameterList(method.Parameters)})
	{{
		using var __writer = new MessageWriter(_allocator, _messageBufferSize, _maximumBufferSize);
		__writer.WriteMethodKey({method.Key});");
			foreach (var param in method.Parameters)
			{
				var serialize = clientModel.Serializer!.Value.IsGeneric
					? $"Serialize<{param.Type.Name}>"
					: $"Serialize{param.Type.EscapedName}";
				clientClass.Append(@$"
		__writer.BeginWriteParameter();
		_serializer.{serialize}(__writer, {param.Name});
		__writer.EndWriteParameter();");
			}
			clientClass.AppendLine(@$"
		await WebSocket.SendAsync(_writer.WrittenMemory, WebSocketMessageType.Binary, true, Disconnected);
	}}");
		}

		//
		// Batching feature
		//
		clientClass.AppendLine(@$"
	/// <summary>
	/// Create and send a batch of websocket-rpc messages to one or more clients.
	/// </summary>
	/// <remarks>
	/// <para>Multiple batches for the same client must not be flushed simultaneously.</para>
	/// <para>Sending the same batch to multiple clients allows you to broadcast messages and only pay for the serialization cost once.</para>
	/// </remarks>
	public sealed class Batch : IDisposable
	{{
		private readonly MessageWriter _writer;");
		if (clientModel.Serializer != null)
		{
			clientClass.AppendLine(@$"
		private readonly {serializerName} _serializer;");
		}
		clientClass.Append(@$"
		/// <summary>
		/// Create a batch from a client, using its configuration for creating the underlying buffer.
		/// </summary>
		/// <param name=""client"">Client to use configuration from.</param>
		public Batch({clientModel.Class.Name} client)
		{{
			_buffer = new MessageWriter(client._allocator, client._batchBufferSize, client._maximumBufferSize);");
		if (clientModel.Serializer != null)
		{
			clientClass.Append(@$"
			_serializer = client._serializer;");
		}
		clientClass.Append(@$"
		}}

		/// <summary>
		/// Create a batch with the provided configuration.
		/// </summary>
		/// <param name=""bufferAllocator"">Array pool to use for allocating buffers</param>
		/// <param name=""initialBufferSize"">Initial size of the batch buffer in bytes. May grow until <paramref name=""maximumBufferSize""/>. Defaults to 32 KiB.</param>
		/// <param name=""maximumBufferSize"">Maximum size of buffers in bytes. Defaults to 16 MiB and only serves as a safeguard against infinite loop bugs.</param>
		public Batch({(clientModel.Serializer != null ? $"{serializerName} serializer, " : string.Empty)}ArrayPool<byte>? bufferAllocator = null, int? initialBufferSize = null, int? maximumBufferSize = null)
		{{
			_buffer = new WebSocketRpcBuffer(bufferAllocator, initialBufferSize ?? 1024 * 32, maximumBufferSize ?? 1024 * 1024 * 16);");
		if (clientModel.Serializer != null)
		{
			clientClass.Append(@$"
			_serializer = serializer;");
		}
		clientClass.AppendLine(@$"
		}}

		public void Dispose()
		{{
			_buffer.Dispose();
		}}

		/// <summary>
		/// Send the accumulated data to the provided client.
		/// </summary>
		/// <param name=""client"">Client to send batch to.</param>
		/// <exception cref=""OperationCanceledException"">Client disconnected or timed out during this operation.</exception>
		/// <exception cref=""WebSocketException"">Operation on the client's websocket failed.</exception>
		public ValueTask SendAsync({clientModel.Class.Name} client)
		{{
			return client.WebSocket.SendAsync(_buffer.AsMemory(), WebSocketMessageType.Binary, true, client.Disconnected);
		}}

		/// <summary>
		/// Send the accumulated data to multiple clients.
		/// </summary>
		/// <exception cref=""AggregateException"">Sending the data to one or more clients failed.</exception>
		public ValueTask SendAsync(IEnumerable<{clientModel.Class.Name}> clients)
		{{
			// TODO: We could reduce allocations here by rolling a list backed by ArrayPool<{clientModel.Class.Name}> in the future
			List<Task>? tasksToAwait = null;
			foreach (var client in clients)
			{{
			 	var valueTask = SendAsync(client);
				if (!valueTask.IsCompleted || valueTask.IsFaulted)
				{{
					// We defer faulted tasks throwing exceptions to allow the data to be sent to the other clients
					(tasksToAwait ??= new()).Add(valueTask.AsTask());
				}}
			}}

			return tasksToAwait == null ? ValueTask.CompletedTask : new ValueTask(Task.WhenAll(tasksToAwait));
		}}");
		foreach (var method in clientModel.Class.Methods)
		{
			clientClass.AppendLine(@$"
		public void {method.Name}({GetParameterList(method.Parameters)})
		{{
			_buffer.WriteMethodKey({method.Key});");
			foreach (var param in method.Parameters)
			{
				//clientClass.AppendLine($"\t\t\t_buffer.WriteParameter(_serializer.{GenerateSerializeCall(param.Type, clientModel.Serializer, param.Name)});");
			}
			clientClass.Append("\t\t}");
		}
		clientClass.AppendLine(@$"
	}}");
		clientClass.Append("}");
		context.AddSource($"{clientModel.Class.Name}.g.cs", clientClass.ToString());
	}

	private sealed class ClientMetadata : MetadataBase
	{
	}
}
