using System.Buffers;

namespace Nickogl.WebSockets.Rpc.LoadTest.App;

public readonly struct PooledByteArray(ArrayPool<byte> allocator, int size) : IDisposable
{
	private readonly ArrayPool<byte> _allocator = allocator;
	private readonly byte[] _bytes = allocator.Rent(size);

	public ReadOnlySpan<byte> Span => _bytes.AsSpan();

	public void Dispose()
	{
		_allocator.Return(_bytes);
	}

	public static implicit operator ReadOnlySpan<byte>(PooledByteArray obj)
	{
		return obj.Span;
	}
}
