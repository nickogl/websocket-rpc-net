using Microsoft.CodeAnalysis;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	private static void GeneratePredefined(IncrementalGeneratorPostInitializationContext context)
	{
		context.AddSource("WebSocketRpcMethodAttribute.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_PREDEFINED
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Mark a method to be called by the websocket server or client. In a server
/// context, it has to additionally take the client as its first parameter. In
/// all contexts it must return a ValueTask.
/// </summary>
/// <param name=""key"">
/// Consistent, unique key of this method. Must be 1 or greater.
/// Changing this value on a method breaks existing clients.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class WebSocketRpcMethodAttribute(int key) : Attribute
{{
	/// <summary>Get the consistent, unique key of this method.</summary>
	public int Key {{ get; }} = key;
}}
#endif");

		context.AddSource("WebSocketRpcSerializationMode.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_PREDEFINED
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Serialization modes for RPC parameters.
/// </summary>
internal enum WebSocketRpcSerializationMode
{{
	/// <summary>Use generic serialization and deserialization methods for all types of parameters.</summary>
	Generic,

	/// <summary>Use specialized serialization and deserialization methods for each type referenced in parameters.</summary>
	Specialized,
}}
#endif");

		context.AddSource("WebSocketRpcClientAttribute.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_PREDEFINED
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Generate a websocket-rpc client implementation in a class. The class must
/// be partial and implement some methods for the generated code. It also has
/// to annotate all methods callable by the server with <see cref=""WebSocketRpcMethodAttribute""/>.
/// </summary>
/// <param name=""serializationMode"">Serialization mode to use for RPC parameters.</param>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class WebSocketRpcClientAttribute(WebSocketRpcSerializationMode serializationMode) : Attribute
{{
	public WebSocketRpcSerializationMode SerializationMode {{ get; }} = serializationMode;
}}
#endif");

		context.AddSource("WebSocketRpcServerAttribute.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_PREDEFINED
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Generate a websocket-rpc server implementation in a class. The class must
/// be partial and implement some methods for the generated code. It also has
/// to annotate all methods callable by the client with <see cref=""WebSocketRpcMethodAttribute""/>.
/// </summary>
/// <param name=""serializationMode"">Serialization mode to use for RPC parameters.</param>
/// <typeparam name=""TClient"">Type of the client connecting to the server.</typeparam>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class WebSocketRpcServerAttribute<TClient>(WebSocketRpcSerializationMode serializationMode) : Attribute
{{
	public WebSocketRpcSerializationMode SerializationMode {{ get; }} = serializationMode;
}}
#endif");

		context.AddSource("WebSocketRpcTestClientAttribute.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_PREDEFINED
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Generate a websocket-rpc test client for calling methods on the server and
/// recording calls received from the server.
/// </summary>
/// <typeparam name=""TServer"">The websocket-rpc server type under test.</typeparam>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class WebSocketRpcTestClientAttribute<TServer> : Attribute
{{
}}
#endif");

		context.AddSource("RpcArgMatcher.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_PREDEFINED
using System;
using System.Collections;
using System.Linq;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Match an argument to a remote procedure call. Used to await specific RPC
/// messages in tests to ensure they are reproducible.
/// </summary>
/// <remarks>
/// Examples:
/// <para><c>await testClient.Receive.Foo(""ExactValue"")</c></para>
/// <para><c>await testClient.Receive.Foo(RpcArg.Is&lt;string&gt;(arg => arg.StartsWith(""MatchesValue"")))</c></para>
/// <para><c>await testClient.Receive.Foo(RpcArg.Any&lt;string&gt;())</c></para>
/// </remarks>
/// <typeparam name=""T"">Type of the argument to match.</typeparam>
internal readonly struct RpcArgMatcher<T>(Func<T, bool> predicate)
{{
	private readonly Func<T, bool> _predicate = predicate;

	public bool Matches(T arg)
	{{
		return _predicate(arg);
	}}

	public static implicit operator RpcArgMatcher<T>(T value)
	{{
		return new RpcArgMatcher<T>(arg =>
		{{
			if (arg == null && value == null)
			{{
				return true;
			}}
			if (arg == null || value == null)
			{{
				return false;
			}}
			if (arg is IEnumerable argList && value is IEnumerable valueList)
			{{
				object[] argArray = [.. argList];
				object[] valueArray = [.. valueList];
				return argArray.SequenceEqual(valueArray);
			}}
			return arg.Equals(value);
		}});
	}}
}}

/// <summary>
/// Helpers to create instances of <see cref=""RpcArgMatcher{{T}}"" />. Usage similar to <c>NSubstitute</c>.
/// </summary>
internal static class RpcArg
{{
	/// <summary>
	/// Create a matcher that only intercepts RPC calls if the provided predicate succeeds.
	/// </summary>
	/// <param name=""predicate"">Predicate to check the transmitted argument.</param>
	/// <typeparam name=""T"">Type of the argument to check.</typeparam>
	/// <returns>An argument matcher for use in <c>await testClient.Receive.Foo(...)</c>.</returns>
	public static RpcArgMatcher<T> Is<T>(Func<T, bool> predicate)
	{{
		return new RpcArgMatcher<T>(predicate);
	}}

	/// <summary>
	/// Create a matcher that always intercepts RPC calls for this argument.
	/// </summary>
	/// <typeparam name=""T"">Type of the argument to check.</typeparam>
	/// <returns>An argument matcher for use in <c>await testClient.Receive.Foo(...)</c>.</returns>
	public static RpcArgMatcher<T> Any<T>()
	{{
		return new RpcArgMatcher<T>(_ => true);
	}}
}}
#endif");

		context.AddSource("IParameterReader.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_PREDEFINED
using System;
using System.IO;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Read parameter data from a websocket-rpc message.
/// </summary>
internal interface IParameterReader
{{
	/// <summary>Get a view of the raw parameter data.</summary>
	ReadOnlySpan<byte> Span {{ get; }}

	/// <summary>Read parameter data using the <see cref=""System.IO.Stream""/> API.</summary>
	/// <remarks>The returned stream is neither writable nor seekable.</remarks>
	Stream Stream {{ get; }}
}}
#endif");

		context.AddSource("IParameterWriter.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_PREDEFINED
using System;
using System.Buffers;
using System.IO;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Write parameter data to a websocket-rpc message.
/// </summary>
internal interface IParameterWriter : IBufferWriter<byte>
{{
	/// <summary>Write parameter data using the <see cref=""System.IO.Stream""/> API.</summary>
	/// <remarks>The returned stream is neither readable nor seekable.</remarks>
	Stream Stream {{ get; }}
}}
#endif");

		context.AddSource("MessageBuffer.g.cs", @$"
#nullable enable

#if !WEBSOCKET_RPC_EXCLUDE_PREDEFINED
using System;
using System.Buffers;
using System.Diagnostics;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Represents an automatically growing, pooled buffer for websocket-rpc message processing.
/// </summary>
/// <remarks>This class is not thread-safe. One may re-use instances with <see cref=""Reset""/>.</remarks>
internal abstract class MessageBuffer : IDisposable
{{
	private readonly ArrayPool<byte> _pool;
	private readonly int _minimumBufferSize;
	private readonly int _maximumBufferSize;
	private byte[]? _buffer;
	private int _offset;

	/// <summary>Get the underlying buffer.</summary>
	protected byte[] Buffer
	{{
		get
		{{
			Debug.Assert(_buffer != null, ""EnsureBuffer() was never called"");
			return _buffer;
		}}
	}}

	/// <summary>Get the current processing position within the buffer.</summary>
	protected int Offset => _offset;

	/// <summary>
	/// Construct a message buffer. This does not yet allocate the underlying buffer.
	/// </summary>
	/// <param name=""pool"">Array pool to pool memory from.</param>
	/// <param name=""minimumBufferSize"">Minimum buffer size. May grow until <paramref name=""maximumBufferSize""/>. Defaults to 1 KiB.</param>
	/// <param name=""maximumBufferSize"">Maximum buffer size. Serves as a safety mechanism. Defaults to <see cref=""Array.MaxLength""/>.</param>
	protected MessageBuffer(ArrayPool<byte>? pool = null, int? minimumBufferSize = null, int? maximumBufferSize = null)
	{{
		_pool = pool ?? ArrayPool<byte>.Shared;
		_minimumBufferSize = minimumBufferSize ?? 1024;
		_maximumBufferSize = maximumBufferSize ?? Array.MaxLength;
	}}

	/// <summary>
	/// Reset this buffer for use in other operations.
	/// </summary>
	public virtual void Reset()
	{{
		if (_buffer != null)
		{{
			_pool.Return(_buffer);
			_buffer = null;
		}}

		_offset = 0;
	}}

	public void Dispose()
	{{
		Reset();
		GC.SuppressFinalize(this);
	}}

	/// <summary>
	/// Ensure that the buffer can hold at least <paramref name=""sizeHint""/> more bytes than its current size.
	/// </summary>
	/// <param name=""sizeHint"">Amount of bytes needed for the next processing step.</param>
	protected void EnsureBuffer(int sizeHint)
	{{
		if (_buffer == null)
		{{
			int initialSize = _minimumBufferSize;
			while (sizeHint > initialSize)
			{{
				initialSize *= 2;
			}}
			_buffer = _pool.Rent(initialSize);
			return;
		}}

		var requiredBufferSize = _offset + sizeHint;
		if (requiredBufferSize > _maximumBufferSize)
		{{
			throw new InvalidOperationException($""Required buffer size of {{requiredBufferSize}} bytes exceeds maximum buffer size of {{_maximumBufferSize}}"");
		}}
		var newBufferSize = _buffer.Length;
		if (requiredBufferSize <= newBufferSize)
		{{
			return;
		}}
		while (requiredBufferSize > newBufferSize)
		{{
			newBufferSize *= 2;
		}}

		var newBuffer = _pool.Rent(newBufferSize);
		_buffer.AsSpan(0, _offset).CopyTo(newBuffer.AsSpan());
		_pool.Return(_buffer);
		_buffer = newBuffer;
		return;
	}}

	/// <summary>
	/// Advance the buffer processing position by <paramref name=""size""/> bytes.
	/// </summary>
	/// <param name=""size"">Amount of bytes to advance.</param>
	protected void AdvanceBuffer(int size)
	{{
		Debug.Assert(_buffer != null, ""EnsureBuffer() was never called"");
		Debug.Assert(_offset + size <= _buffer.Length, ""Cannot advance buffer beyond its capacity"");

		_offset += size;
	}}
}}
#endif");

		context.AddSource("MessageReader.g.cs", @$"
#nullable enable

#if !WEBSOCKET_RPC_EXCLUDE_PREDEFINED
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Read methods and their parameters from websocket-rpc messages.
/// </summary>
/// <remarks>This class is not thread-safe.</remarks>
internal sealed class MessageReader : MessageBuffer, IParameterReader
{{
	private int _readOffset;
	private int _currentParameterSize;
	private StreamWrapper? _stream;

	public ReadOnlySpan<byte> Span => new(Buffer, _readOffset, _currentParameterSize);

	public Stream Stream => _stream ??= new StreamWrapper(this);

	/// <summary>Whether or not the end of the message was reached.</summary>
	public bool EndOfMessage => _readOffset == Buffer.Length;

	/// <inheritdoc/>
	public MessageReader(ArrayPool<byte>? pool = null, int? minimumBufferSize = null, int? maximumBufferSize = null)
		: base(pool, minimumBufferSize, maximumBufferSize)
	{{
	}}

	public override void Reset()
	{{
		base.Reset();

		_readOffset = 0;
		_stream?.Reset();
	}}

	/// <summary>
	/// Read the next method key from the received websocket-rpc message.
	/// </summary>
	/// <returns>The method key greater than or equal to 0.</returns>
	public int ReadMethodKey()
	{{
		Debug.Assert(Offset != 0, ""AdvanceReceiveBuffer() was never called"");
		if (Buffer.Length - _readOffset < sizeof(int))
		{{
			throw new InvalidDataException(""Not enough data was received from the websocket to read the method key"");
		}}

		var methodKey = BinaryPrimitives.ReadInt32LittleEndian(Buffer.AsSpan(_readOffset, sizeof(int)));
		_readOffset += sizeof(int);
		return methodKey;
	}}

	/// <summary>
	/// Begin reading a parameter. Must have an accompanying call to <see cref=""EndReadParameter""/>
	/// after reading the parameter is complete.
	/// </summary>
	/// <remarks>Use <see cref=""Span""/> or <see cref=""Stream""/> to access the parameter data.</remarks>
	public void BeginReadParameter()
	{{
		Debug.Assert(Offset != 0, ""AdvanceReceiveBuffer() was never called"");
		Debug.Assert(_readOffset >= 4, ""ReadMethodKey() was never called"");
		if (Buffer.Length - _readOffset < sizeof(int))
		{{
			throw new InvalidDataException(""Not enough data was received from the websocket to read the parameter"");
		}}

		_currentParameterSize = BinaryPrimitives.ReadInt32LittleEndian(Buffer.AsSpan(_readOffset, sizeof(int)));
		_readOffset += sizeof(int);
	}}

	/// <summary>
	/// End reading a parameter previously initiated with <see cref=""BeginReadParameter""/>.
	/// </summary>
	public void EndReadParameter()
	{{
		_readOffset += _currentParameterSize;
	}}

	/// <summary>
	/// Advance the receive buffer by the actual amount of data read from the websocket.
	/// </summary>
	/// <param name=""count"">Amount of bytes to advance the buffer by.</param>
	public void AdvanceReceiveBuffer(int count)
	{{
		AdvanceBuffer(count);
	}}

	/// <summary>
	/// Get a buffer for writing data received from a websocket.
	/// </summary>
	/// <param name=""minSize"">Minimum size of the receive buffer. Defaults to 1 KiB.</param>
	/// <returns>A buffer of at least <paramref name=""minSize""/> to use for receiving websocket data.</returns>
	public Memory<byte> GetReceiveBuffer(int minSize = 1024)
	{{
		EnsureBuffer(minSize);

		return Buffer.AsMemory(Offset);
	}}

	private sealed class StreamWrapper(MessageReader reader) : Stream
	{{
		private readonly MessageReader _reader = reader;
		private int _innerReadOffset;

		public void Reset()
		{{
			_innerReadOffset = 0;
		}}

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => _reader._currentParameterSize;
		public override long Position {{ get => _innerReadOffset; set => throw new NotImplementedException(); }}
		public override void Flush() => throw new NotImplementedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
		public override void SetLength(long value) => throw new NotImplementedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

		public override int Read(byte[] buffer, int offset, int count)
		{{
			return Read(buffer.AsSpan(offset, count));
		}}

		public override int Read(Span<byte> buffer)
		{{
			if (buffer.Length == 0)
			{{
				return 0;
			}}

			int maxAvailable = _reader.Span.Length - _innerReadOffset;
			int readCount = Math.Min(buffer.Length, maxAvailable);
			_reader.Span.Slice(_innerReadOffset, readCount).CopyTo(buffer);
			_innerReadOffset += readCount;
			return readCount;
		}}
	}}
}}
#endif");

		context.AddSource("MessageWriter.g.cs", @$"
#nullable enable

#if !WEBSOCKET_RPC_EXCLUDE_PREDEFINED
using System;
using System.Buffers;
using System.Buffers.Binary;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Write methods and their parameters to websocket-rpc messages.
/// </summary>
/// <remarks>This class is not thread-safe.</remarks>
internal sealed class MessageWriter : MessageBuffer, IParameterWriter
{{
	private int _lastOffset;
	private StreamWrapper? _stream;

	public Stream Stream => _stream ??= new StreamWrapper(this);

	/// <summary>Get the amount of data written to the underlying buffer.</summary>
	public int WrittenCount => Offset;

	/// <summary>Get a <see cref=""ReadOnlyMemory{{byte}}""/> that contains the data written to the underlying buffer so far.</summary>
	public ReadOnlyMemory<byte> WrittenMemory => new(Buffer, 0, Offset);

	/// <summary>Get a <see cref=""ReadOnlySpan{{byte}}""/> that contains the data written to the underlying buffer so far.</summary>
	public ReadOnlySpan<byte> WrittenSpan => new(Buffer, 0, Offset);

	/// <inheritdoc/>
	public MessageWriter(ArrayPool<byte>? pool = null, int? minimumBufferSize = null, int? maximumBufferSize = null)
		: base(pool, minimumBufferSize, maximumBufferSize)
	{{
	}}

	/// <summary>
	/// Write the provided method key to the websocket-rpc message.
	/// </summary>
	/// <param name=""methodKey"">Key of the method to call on the client.</param>
	public void WriteMethodKey(int methodKey)
	{{
		var destination = GetSpan(sizeof(int));
		BinaryPrimitives.WriteInt32LittleEndian(destination, methodKey);
	}}

	/// <summary>
	/// Begin writing a parameter. Must have an accompanying call to <see cref=""EndWriteParameter""/>
	/// after writing the parameter is complete.
	/// </summary>
	public void BeginWriteParameter()
	{{
		_lastOffset = Offset;

		// Skip writing the parameter size for later, as we do not know the size of the parameter yet
		EnsureBuffer(sizeof(int));
		AdvanceBuffer(sizeof(int));
	}}

	/// <summary>
	/// End writing the parameter previously initiated with <see cref=""BeginWriteParameter""/>.
	/// </summary>
	public void EndWriteParameter()
	{{
		// Retrospectively write the size of the parameter into the buffer
		int parameterSize = Offset - _lastOffset - 4;
		var destination = Buffer.AsSpan(_lastOffset, sizeof(int));
		BinaryPrimitives.WriteInt32LittleEndian(destination, parameterSize);
	}}

	public void Advance(int count)
	{{
		AdvanceBuffer(count);
	}}

	public Memory<byte> GetMemory(int sizeHint = 0)
	{{
		sizeHint = sizeHint == 0 ? 1 : sizeHint;
		EnsureBuffer(sizeHint);

		return new Memory<byte>(Buffer, Offset, sizeHint);
	}}

	public Span<byte> GetSpan(int sizeHint = 0)
	{{
		sizeHint = sizeHint == 0 ? 1 : sizeHint;
		EnsureBuffer(sizeHint);

		return new Span<byte>(Buffer, Offset, sizeHint);
	}}

	private sealed class StreamWrapper(MessageWriter writer) : Stream
	{{
		private readonly MessageWriter _writer = writer;

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => _writer.Offset;
		public override long Position {{ get => _writer.Offset; set => throw new NotImplementedException(); }}
		public override void Flush() => throw new NotImplementedException();
		public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
		public override void SetLength(long value) => throw new NotImplementedException();

		public override void Write(byte[] buffer, int offset, int count)
		{{
			Write(new ReadOnlySpan<byte>(buffer, offset, count));
		}}

		public override void Write(ReadOnlySpan<byte> buffer)
		{{
			var destination = _writer.GetSpan(buffer.Length);
			buffer.CopyTo(destination);
			_writer.Advance(buffer.Length);
		}}
	}}
}}
#endif");
	}
}
