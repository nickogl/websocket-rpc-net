using System.Buffers.Binary;
using System.Diagnostics;

namespace Nickogl.WebSockets.Rpc.Serialization;

/// <summary>
/// Default implementation for writing RPC messages.
/// </summary>
public sealed class RpcMessageWriter(RpcMessageBufferOptions bufferOptions) : IRpcMessageWriter, IRpcParameterWriter
{
	// Do not change to readonly, as this is a mutable struct
	private RpcMessageBuffer _buffer = new(bufferOptions);
	private int _parameterOffset = -1;
	private RpcParameterStream? _parameterStream;

	/// <inheritdoc/>
	public int WrittenCount => _buffer.Consumed;

	/// <inheritdoc/>
	public ReadOnlyMemory<byte> WrittenMemory => _buffer.Memory[.._buffer.Consumed];

	/// <inheritdoc/>
	public ReadOnlySpan<byte> WrittenSpan => _buffer.Span[.._buffer.Consumed];

	/// <inheritdoc/>
	public IRpcParameterWriter ParameterWriter
	{
		get
		{
			// We implement the interface in this class to avoid an extra allocation
			Debug.Assert(_parameterOffset >= 0, "BeginWriteParameter() was never called");
			return this;
		}
	}

	/// <inheritdoc/>
	public Stream Stream
	{
		get
		{
			Debug.Assert(_parameterOffset >= 0, "BeginWriteParameter() was never called");
			return _parameterStream ??= new RpcParameterStream(this);
		}
	}

	/// <summary>
	/// Performs the same operations as <see cref="Reset"/>.
	/// </summary>
	public void Dispose()
	{
		Reset();
	}

	/// <inheritdoc/>
	public void Reset()
	{
		_buffer.Reset();

#if DEBUG
		_parameterOffset = -1;
#endif
		_parameterStream?.Reset();
	}

	/// <inheritdoc/>
	public void WriteMethodKey(int key)
	{
		Debug.Assert(key >= 0, "Key must not be negative");
		Debug.Assert(_parameterOffset == -1, "Cannot write method key while writing a parameter, did you forget to call EndWriteParameter()?");

		_buffer.EnsureAtLeast(sizeof(int));
		var destination = _buffer.Span.Slice(_buffer.Consumed, sizeof(int));
		BinaryPrimitives.WriteInt32LittleEndian(destination, key);
		_buffer.Consume(sizeof(int));
	}

	/// <inheritdoc/>
	public void BeginWriteParameter()
	{
		Debug.Assert(_buffer.Consumed >= 4, "WriteMethodKey() was never called");
		Debug.Assert(_parameterOffset == -1, "Already writing a parameter, did you forget to call EndWriteParameter()?");

		// Skip writing the parameter size for later, as we do not know it yet
		_buffer.EnsureAtLeast(sizeof(int));
		_buffer.Consume(sizeof(int));

		_parameterOffset = _buffer.Consumed;
	}

	/// <inheritdoc/>
	public void EndWriteParameter()
	{
		Debug.Assert(_parameterOffset >= 0, "BeginReadParameter() was never called");

		// Retrospectively write the size of the parameter into the buffer
		int parameterSize = _buffer.Consumed - _parameterOffset;
		var destination = _buffer.Span.Slice(_parameterOffset - 4, sizeof(int));
		BinaryPrimitives.WriteInt32LittleEndian(destination, parameterSize);
#if DEBUG
		_parameterOffset = -1;
#endif
		_parameterStream?.Reset();
	}

	/// <inheritdoc/>
	public void Advance(int count)
	{
		_buffer.Consume(count);
	}

	/// <inheritdoc/>
	public Memory<byte> GetMemory(int sizeHint = 0)
	{
		sizeHint = sizeHint == 0 ? 1 : sizeHint;
		_buffer.EnsureAtLeast(sizeHint);
		return _buffer.Memory[_buffer.Consumed..];
	}

	/// <inheritdoc/>
	public Span<byte> GetSpan(int sizeHint = 0)
	{
		sizeHint = sizeHint == 0 ? 1 : sizeHint;
		_buffer.EnsureAtLeast(sizeHint);
		return _buffer.Span[_buffer.Consumed..];
	}

	private sealed class RpcParameterStream(RpcMessageWriter writer) : Stream
	{
		private readonly RpcMessageWriter _writer = writer;
		private int _position;

		public void Reset()
		{
			_position = 0;
		}

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => _position;

		public override long Position { get => _position; set => throw new NotImplementedException(); }

		public override void Flush() => throw new NotImplementedException();

		public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();

		public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();

		public override void SetLength(long value) => throw new NotImplementedException();

		public override void Write(byte[] buffer, int offset, int count)
		{
			Write(new ReadOnlySpan<byte>(buffer, offset, count));
		}

		public override void Write(ReadOnlySpan<byte> buffer)
		{
			var destination = _writer.GetSpan(buffer.Length);
			buffer.CopyTo(destination);
			_writer.Advance(buffer.Length);

			_position += buffer.Length;
		}
	}
}
