using Nickogl.WebSockets.Rpc.Serialization;

namespace Nickogl.WebSockets.Rpc.UnitTest.Tests;

public class RpcMessageBufferTests
{
	[Fact]
	public void AllocatesBufferOfMinimumSize()
	{
		using var pool = new DeterministicArrayPool<byte>();
		using var buffer = new RpcMessageBuffer(new()
		{
			Pool = pool,
			MinimumSize = 16,
			MaximumSize = Array.MaxLength,
		});

		buffer.EnsureAtLeast(1);

		Assert.Equal(16, buffer.Span.Length);
	}

	[Fact]
	public void GrowsBufferExpontentially()
	{
		using var pool = new DeterministicArrayPool<byte>();
		using var buffer = new RpcMessageBuffer(new()
		{
			Pool = pool,
			MinimumSize = 16,
			MaximumSize = Array.MaxLength,
		});

		buffer.EnsureAtLeast(20);
		Assert.Equal(32, buffer.Span.Length);
		buffer.Consume(20);

		buffer.EnsureAtLeast(20);
		Assert.Equal(64, buffer.Span.Length);
		buffer.Consume(20);

		buffer.EnsureAtLeast(20);
		Assert.Equal(64, buffer.Span.Length);
		buffer.Consume(20);

		buffer.EnsureAtLeast(4);
		Assert.Equal(64, buffer.Span.Length);
		buffer.Consume(4);

		buffer.EnsureAtLeast(1);
		Assert.Equal(128, buffer.Span.Length);
		buffer.Consume(1);
	}

	[Fact]
	public void AllowsReuseAfterReset()
	{
		using var pool = new DeterministicArrayPool<byte>();
		using var buffer = new RpcMessageBuffer(new()
		{
			Pool = pool,
			MinimumSize = 16,
			MaximumSize = Array.MaxLength,
		});

		buffer.EnsureAtLeast(20);
		Assert.Equal(32, buffer.Span.Length);
		buffer.Consume(20);

		buffer.Reset();

		// If it were not reset correctly, it would grow to 64 bytes
		buffer.EnsureAtLeast(20);
		Assert.Equal(32, buffer.Span.Length);
		buffer.Consume(20);
	}

	[Fact]
	public void CopiesDataDuringResize()
	{
		using var pool = new DeterministicArrayPool<byte>();
		using var buffer = new RpcMessageBuffer(new()
		{
			Pool = pool,
			MinimumSize = 4,
			MaximumSize = Array.MaxLength,
		});

		buffer.EnsureAtLeast(4);
		buffer.Span.Slice(buffer.Consumed, 2).Fill(0x9f);
		buffer.Consume(2);
		buffer.EnsureAtLeast(4);
		buffer.Span.Slice(buffer.Consumed, 4).Fill(0xc8);
		buffer.Consume(4);

		ReadOnlySpan<byte> expected = [0x9f, 0x9f, 0xc8, 0xc8, 0xc8, 0xc8];
		Assert.Equal(expected, buffer.Span[..buffer.Consumed]);
	}

	[Fact]
	public void LimitsBufferToMaximumSize()
	{
		using var pool = new DeterministicArrayPool<byte>();
		using var buffer = new RpcMessageBuffer(new()
		{
			Pool = pool,
			MinimumSize = 8,
			MaximumSize = 8,
		});

		Assert.Throws<InvalidOperationException>(() => buffer.EnsureAtLeast(9));

		buffer.EnsureAtLeast(4);
		buffer.Consume(4);
		Assert.Throws<InvalidOperationException>(() => buffer.EnsureAtLeast(5));
	}
}
