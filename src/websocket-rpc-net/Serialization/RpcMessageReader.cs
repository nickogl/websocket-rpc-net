using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;

namespace Nickogl.WebSockets.Rpc.Serialization;

/// <summary>
/// Default implementation for reading RPC messages.
/// </summary>
public sealed class RpcMessageReader(RpcMessageBufferOptions bufferOptions) : IRpcMessageReader, IRpcParameterReader, IBufferWriter<byte>
{
	// Do not change to readonly, as this is a mutable struct
	private RpcMessageBuffer _buffer = new(bufferOptions);
	private int _offset;
	private int _parameterSize = -1;
	private RpcParameterStream? _parameterStream;

	/// <inheritdoc/>
	public IBufferWriter<byte> ReceiveBuffer
	{
		get
		{
			// We implement the interface in this class to avoid an extra allocation
			Debug.Assert(_offset == 0, "Cannot use the receive buffer while reading a message, did you forget to call Reset()?");
			return this;
		}
	}

	/// <inheritdoc/>
	public bool EndOfMessage => _offset == _buffer.Consumed;

	/// <inheritdoc/>
	public IRpcParameterReader ParameterReader
	{
		get
		{
			// We implement the interface in this class to avoid an extra allocation
			Debug.Assert(_parameterSize >= 0, "BeginReadParameter() was never called");
			return this;
		}
	}

	/// <inheritdoc/>
	public ReadOnlyMemory<byte> Memory
	{
		get
		{
			Debug.Assert(_parameterSize >= 0, "BeginReadParameter() was never called");
			return _buffer.Memory.Slice(_offset, _parameterSize);
		}
	}

	/// <inheritdoc/>
	public ReadOnlySpan<byte> Span
	{
		get
		{
			Debug.Assert(_parameterSize >= 0, "BeginReadParameter() was never called");
			return _buffer.Span.Slice(_offset, _parameterSize);
		}
	}

	/// <inheritdoc/>
	public Stream Stream
	{
		get
		{
			Debug.Assert(_parameterSize >= 0, "BeginReadParameter() was never called");
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
		_offset = 0;

		_parameterStream?.Reset();
#if DEBUG
		_parameterSize = -1;
#endif
	}

	/// <inheritdoc/>
	public int ReadMethodKey()
	{
		Debug.Assert(_parameterSize == -1, "Cannot read method key while reading a parameter, did you forget to call EndReadParameter()?");
		var remaining = _buffer.Span[_offset.._buffer.Consumed];
		if (remaining.Length < sizeof(int))
		{
			throw new InvalidDataException("Not enough data was received from the websocket to read a method key");
		}

		var key = BinaryPrimitives.ReadInt32LittleEndian(remaining);
		_offset += sizeof(int);
		return key;
	}

	/// <inheritdoc/>
	public void BeginReadParameter()
	{
		Debug.Assert(_offset >= 4, "ReadMethodKey() was never called");
		Debug.Assert(_parameterSize == -1, "Already reading a parameter, did you forget to call EndReadParameter()?");
		var remaining = _buffer.Span[_offset.._buffer.Consumed];
		if (remaining.Length < sizeof(int))
		{
			throw new InvalidDataException("Not enough data was received from the websocket to read the parameter's length");
		}

		var size = BinaryPrimitives.ReadInt32LittleEndian(remaining);
		if (size < 0)
		{
			throw new InvalidDataException("Received negative parameter length");
		}
		if (remaining.Length - sizeof(int) < size)
		{
			throw new InvalidDataException("Not enough data was received from the websocket to read the parameter's data");
		}

		_parameterSize = size;
		_offset += sizeof(int);
	}

	/// <inheritdoc/>
	public void EndReadParameter()
	{
		Debug.Assert(_parameterSize >= 0, "BeginReadParameter() was never called");

		_offset += _parameterSize;
#if DEBUG
		_parameterSize = -1;
#endif
		_parameterStream?.Reset();
	}

	/// <summary>
	/// Advance the <see cref="ReceiveBuffer"/> by <paramref name="count"/> bytes written.
	/// </summary>
	/// <param name="count">Amount of bytes written to the <see cref="ReceiveBuffer"/>.</param>
	public void Advance(int count)
	{
		Debug.Assert(_offset == 0, "Cannot use the receive buffer while reading a message, did you forget to call Reset()?");

		_buffer.Consume(count);
	}

	/// <summary>
	/// Get the next <see cref="Memory{T}"/> of the <see cref="ReceiveBuffer"/> to write to that is at least <paramref name="sizeHint"/> bytes large.
	/// </summary>
	/// <param name="sizeHint">The minimum length of the returned <see cref="Memory{T}"/>.</param>
	public Memory<byte> GetMemory(int sizeHint = 0)
	{
		Debug.Assert(_offset == 0, "Cannot use the receive buffer while reading a message, did you forget to call Reset()?");

		sizeHint = sizeHint == 0 ? 1 : sizeHint;
		_buffer.EnsureAtLeast(sizeHint);
		return _buffer.Memory[_buffer.Consumed..];
	}

	/// <summary>
	/// Get the next <see cref="Span{T}"/> of the <see cref="ReceiveBuffer"/> to write to that is at least <paramref name="sizeHint"/> bytes large.
	/// </summary>
	/// <param name="sizeHint">The minimum length of the returned <see cref="Span{T}"/>.</param>
	public Span<byte> GetSpan(int sizeHint = 0)
	{
		Debug.Assert(_offset == 0, "Cannot use the receive buffer while reading a message, did you forget to call Reset()?");

		sizeHint = sizeHint == 0 ? 1 : sizeHint;
		_buffer.EnsureAtLeast(sizeHint);
		return _buffer.Span[_buffer.Consumed..];
	}

	private sealed class RpcParameterStream(RpcMessageReader reader) : Stream
	{
		private readonly RpcMessageReader _reader = reader;
		private int _position;

		public void Reset()
		{
			_position = 0;
		}

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => _reader._parameterSize;

		public override long Position { get => _position; set => throw new NotImplementedException(); }

		public override void Flush() => throw new NotImplementedException();

		public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();

		public override void SetLength(long value) => throw new NotImplementedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

		public override int Read(byte[] buffer, int offset, int count)
		{
			return Read(buffer.AsSpan(offset, count));
		}

		public override int Read(Span<byte> buffer)
		{
			if (buffer.Length == 0)
			{
				return 0;
			}

			int maxAvailable = _reader.Span.Length - _position;
			int readCount = Math.Min(buffer.Length, maxAvailable);
			_reader.Span.Slice(_position, readCount).CopyTo(buffer);

			_position += readCount;

			return readCount;
		}
	}
}
