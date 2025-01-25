using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	private static ClientModel? ExtractClientModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
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

		var clientClass = new StringBuilder(@$"
#nullable enable

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

/// <inheritdoc />
/// <remarks>The auto-generated portion of this class is not thread-safe and should not be used from multiple threads concurrently.</remarks>
partial class {clientModel.Class.Name} : IDisposable
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

	/// <summary>Currently allocated buffer for writing batches or standalone messages.</summary>
	/// <remarks>Please return this buffer to the <see cref=""_allocator"" /> in the <see cref=""IDisposable"" /> implementation.</remarks>
	private byte[]? _buffer;
	private int _bufferOffset;

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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void __BufferEnsureAtLeast(int size)
	{{
		Debug.Assert(_buffer != null);

		var requiredBufferSize = _bufferOffset + size;
		Debug.Assert(requiredBufferSize <= _maximumBufferSize, $""Requested buffer size ({{requiredBufferSize}} bytes) exceeds maximum buffer size ({{_maximumBufferSize}} bytes). Check if you have an infinite loop and if not, increase _maximumBufferSize to at least {{requiredBufferSize}}."");
		var actualBufferSize = _buffer.Length;
		if (requiredBufferSize <= actualBufferSize)
		{{
			return;
		}}
		while (requiredBufferSize > actualBufferSize)
		{{
			actualBufferSize *= 2;
		}}

		var newBuffer = _allocator.Rent(actualBufferSize);
		_buffer.AsSpan(0, _bufferOffset).CopyTo(newBuffer.AsSpan());
		_allocator.Return(_buffer);

		_buffer = newBuffer;
	}}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void __BufferWriteMethodKey(int methodKey)
	{{
		Debug.Assert(_buffer != null);

		__BufferEnsureAtLeast(sizeof(int));

		BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_bufferOffset, sizeof(int)), methodKey);
		_bufferOffset += sizeof(int);
	}}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void __BufferWriteParameter(ReadOnlySpan<byte> data)
	{{
		Debug.Assert(_buffer != null);

		var fullParamSize = sizeof(int) + data.Length;
		__BufferEnsureAtLeast(fullParamSize);

		BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_bufferOffset, sizeof(int)), data.Length);
		data.CopyTo(_buffer.AsSpan(_bufferOffset, fullParamSize));
		_bufferOffset += fullParamSize;
	}}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void __BufferCreate(int bufferSize)
	{{
		Debug.Assert(_buffer == null, ""Can send only one message or one batch or one broadcast at a time"");

		_buffer = _allocator.Rent(bufferSize);
		_bufferOffset = 0;
	}}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void __BufferDestroy()
	{{
		Debug.Assert(_buffer != null, ""Batch or broadcast seems to have been disposed of twice"");

		_allocator.Return(_buffer);
		_buffer = null;
	}}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private ReadOnlyMemory<byte> __BufferGetView()
	{{
		Debug.Assert(_buffer != null, ""No message or batch or broadcast is currently active"");

		return new(_buffer, 0, _bufferOffset);
	}}
");
		if (clientModel.Serializer != null)
		{
			clientClass.AppendLine(@$"
	/// <summary>Serializer to serialize and deserialize RPC parameters.</summary>
	/// <remarks>Initialize this field in the constructor of your class.</remarks>
	private {GetFullyQualifiedType(clientModel.Serializer.Value.InterfaceNamespace, clientModel.Serializer.Value.InterfaceName)} _serializer;");
		}
		foreach (var method in clientModel.Class.Methods)
		{
			clientClass.AppendLine(@$"
	public partial async ValueTask {method.Name}({GenerateParameterList(method.Parameters)})
	{{
		__BufferCreate(_messageBufferSize);
		try
		{{
			__BufferWriteMethodKey({method.Key});");
			foreach (var param in method.Parameters)
			{
				clientClass.AppendLine($"\t\t\t__BufferWriteParameter(_serializer.{GenerateSerializeCall(param.Type, clientModel.Serializer, param.Name)});");
			}
			clientClass.AppendLine(@$"
			await WebSocket.SendAsync(__BufferGetView(), WebSocketMessageType.Binary, true, Disconnected);
		}}
		finally
		{{
			__BufferDestroy();
		}}
	}}");
		}

		//
		// Batching feature
		//
		clientClass.AppendLine(@$"
	/// <summary>
	/// Create and send a batch of websocket-rpc messages.
	/// There can only be a single one in use per client at any time.
	/// </summary>
	/// <remarks>Contrary to stream implementations, disposing of the batch does not flush the accumulated data.</remarks>
	public struct Batch : IDisposable
	{{
		private readonly {clientModel.Class.Name} _client;

		public Batch({clientModel.Class.Name} client)
		{{
			_client = client;
			_client.__BufferCreate(client._batchBufferSize);
		}}

		public void Dispose()
		{{
			_client.__BufferDestroy();
		}}

		/// <summary>
		/// Send the accumulated data to the client.
		/// </summary>
		public ValueTask Flush()
		{{
			return _client.WebSocket.SendAsync(_client.__BufferGetView(), WebSocketMessageType.Binary, true, _client.Disconnected);
		}}");
		foreach (var method in clientModel.Class.Methods)
		{
			clientClass.AppendLine(@$"
		public void {method.Name}({GenerateParameterList(method.Parameters)})
		{{
			_client.__BufferWriteMethodKey({method.Key});");
			foreach (var param in method.Parameters)
			{
				clientClass.AppendLine($"\t\t\t_client.__BufferWriteParameter(_client._serializer.{GenerateSerializeCall(param.Type, clientModel.Serializer, param.Name)});");
			}
			clientClass.Append("\t\t}");
		}
		clientClass.AppendLine(@$"
	}}");

		//
		// Broadcasting feature
		//
		clientClass.AppendLine(@$"
	/// <summary>
	/// Broadcast the same websocket-rpc messages to multiple clients.
	/// This has the benefit of paying for the serialization cost only once.
	/// There can be multiple in use at the same time provided that clients are
	/// part of only one broadcast.
	/// </summary>
	/// <remarks>Contrary to stream implementations, disposing of the broadcast does not flush the accumulated data.</remarks>
	public struct Broadcast : IDisposable
	{{
		private readonly ICollection<{clientModel.Class.Name}> _seenClients;

		/// <summary>
		/// Create a new broadcast. It allocates a new collection to keep track of seen clients.
		/// </summary>
		public Broadcast()
		{{
			_seenClients = [];
		}}

		/// <summary>
		/// Create a new broadcast and use the provided buffer to keep track of seen clients.
		/// To reduce allocations, keep recycling N buffers for N concurrent broadcasts.
		/// </summary>
		/// <param name=""seenClientsBuffer"">Buffer to keep track of seen clients. MUST BE EMPTY.</param>
		public Broadcast(ICollection<{clientModel.Class.Name}> seenClientsBuffer)
		{{
			Debug.Assert(seenClientsBuffer.Count == 0, ""Provided buffer for keeping track of seen clients must be empty"");

			_seenClients = seenClientsBuffer;
		}}

		public void Dispose()
		{{
			foreach (var client in _seenClients)
			{{
				client.__BufferDestroy();
			}}
			_seenClients.Clear();
		}}

		/// <summary>
		/// Send the accumulated data to the clients.
		/// </summary>
		/// <exception cref=""AggregateException"">Sending the data to one or more clients failed.</exception>
		public async ValueTask Flush()
		{{
			var tasksToAwait = new List<Task>();
			foreach (var client in _seenClients)
			{{
			 	var task = client.WebSocket.SendAsync(client.__BufferGetView(), WebSocketMessageType.Binary, true, client.Disconnected);
				if (!task.IsCompleted || task.IsFaulted)
				{{
					// We defer faulted tasks throwing exceptions to allow the data to be sent to the other clients
					tasksToAwait.Add(task.AsTask());
				}}
			}}
			if (tasksToAwait.Count > 0)
			{{
				await Task.WhenAll(tasksToAwait);
			}}
		}}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void EnsureClient({clientModel.Class.Name} client)
		{{
			if (client._buffer == null)
			{{
				client.__BufferCreate(client._batchBufferSize);
				_seenClients.Add(client);
			}}
		}}");
		foreach (var method in clientModel.Class.Methods)
		{
			clientClass.AppendLine(@$"
		public void {method.Name}(IEnumerable<{clientModel.Class.Name}> clients, {GenerateParameterList(method.Parameters)})
		{{
			var __firstClient = clients.FirstOrDefault();
			if (__firstClient == null)
			{{
				return;
			}}");
			foreach (var param in method.Parameters)
			{
				clientClass.Append(@$"
			var __{param.Name}Data = __firstClient._serializer.{GenerateSerializeCall(param.Type, clientModel.Serializer, param.Name)};");
			}
			clientClass.AppendLine(@$"
			foreach (var __client in clients)
			{{
				EnsureClient(__client);

				__client.__BufferWriteMethodKey({method.Key});");
			foreach (var param in method.Parameters)
			{
				clientClass.AppendLine($"\t\t\t\t__client.__BufferWriteParameter(__{param.Name}Data);");
			}
			clientClass.Append(@$"
			}}
		}}");
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
