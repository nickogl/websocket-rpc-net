using System.Diagnostics;

namespace Nickogl.WebSockets.Rpc.Serialization;

/// <summary>
/// Create an automatically growing, pooled buffer for processing RPC messages.
/// </summary>
public struct RpcMessageBuffer(RpcMessageBufferOptions options) : IDisposable
{
	private readonly RpcMessageBufferOptions _options = options;
	private byte[]? _buffer;
	private int _consumed;

	/// <summary>Get a <see cref="Memory{T}"/> over the entire buffer, including excess bytes.</summary>
	public readonly Memory<byte> Memory
	{
		get
		{
			Debug.Assert(_buffer is not null, "EnsureAtLeast() was never called");
			return _buffer.AsMemory();
		}
	}

	/// <summary>Get a <see cref="Span{T}"/> over the entire buffer, including excess bytes.</summary>
	public readonly Span<byte> Span
	{
		get
		{
			Debug.Assert(_buffer is not null, "EnsureAtLeast() was never called");
			return _buffer.AsSpan();
		}
	}

	/// <summary>Get the amount of consumed bytes from the buffer.</summary>
	public readonly int Consumed
	{
		get
		{
			Debug.Assert(_buffer is not null, "EnsureAtLeast() was never called");
			return _consumed;
		}
	}

	/// <summary>
	/// Performs the same operations as <see cref="Reset"/>.
	/// </summary>
	public void Dispose()
	{
		Reset();
	}

	/// <summary>
	/// Reset the buffer so that it can be re-used later.
	/// </summary>
	public void Reset()
	{
		if (_buffer is not null)
		{
			_options.Pool.Return(_buffer);
			_buffer = null;
		}

		_consumed = 0;
	}

	/// <summary>
	/// Ensure that the buffer can hold at least <paramref name="count"/> bytes beyond the current position.
	/// </summary>
	/// <param name="count">Amount of bytes needed for the next processing step.</param>
	public void EnsureAtLeast(int count)
	{
		Debug.Assert(count > 0, "Size hint must be at least one byte");

		if (_buffer is null)
		{
			int initialSize = _options.MinimumSize;
			while (count > initialSize)
			{
				initialSize *= 2;
			}
			if (initialSize > _options.MaximumSize)
			{
				throw new InvalidOperationException($"Initial buffer size of {initialSize} bytes exceeds maximum buffer size of {_options.MaximumSize}");
			}
			_buffer = _options.Pool.Rent(initialSize);
			return;
		}

		var requiredBufferSize = _consumed + count;
		var newBufferSize = _buffer.Length;
		if (requiredBufferSize <= newBufferSize)
		{
			return;
		}
		do { newBufferSize *= 2; } while (requiredBufferSize > newBufferSize);
		if (newBufferSize > _options.MaximumSize)
		{
			throw new InvalidOperationException($"New buffer size of {newBufferSize} bytes exceeds maximum buffer size of {_options.MaximumSize}");
		}

		var newBuffer = _options.Pool.Rent(newBufferSize);
		_buffer.AsSpan(0, _consumed).CopyTo(newBuffer.AsSpan());
		_options.Pool.Return(_buffer);
		_buffer = newBuffer;
	}

	/// <summary>
	/// Mark <paramref name="count"/> bytes as consumed.
	/// </summary>
	/// <param name="count">Amount of bytes to consume.</param>
	public void Consume(int count)
	{
		Debug.Assert(count > 0, "Size must be at least one byte");
		Debug.Assert(_buffer is not null, "EnsureAtLeast() was never called");
		Debug.Assert(_consumed + count <= _buffer.Length, "Cannot advance buffer beyond its capacity");

		_consumed += count;
	}
}
