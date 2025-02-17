using Nickogl.WebSockets.Rpc.Serialization;

namespace Nickogl.WebSockets.Rpc.UnitTest.Tests;

public class RpcMessageReaderTests : IDisposable
{
	private readonly DeterministicArrayPool<byte> _bufferPool;
	private readonly RpcMessageBufferOptions _bufferOptions;

	public RpcMessageReaderTests()
	{
		_bufferPool = new DeterministicArrayPool<byte>();
		_bufferOptions = new RpcMessageBufferOptions()
		{
			Pool = _bufferPool,
			MinimumSize = 1024,
			MaximumSize = 1024 * 1024,
		};
	}

	public void Dispose()
	{
		_bufferPool.Dispose();

		GC.SuppressFinalize(this);
	}

	[Fact]
	public void FailsReadingMethodKeyIfInsufficientData()
	{
		using var reader = new RpcMessageReader(_bufferOptions);

		InitializeReader(reader, [0x0]);

		Assert.Throws<InvalidDataException>(() => reader.ReadMethodKey());
	}

	[Fact]
	public void ReadsMethodKey()
	{
		using var reader = new RpcMessageReader(_bufferOptions);
		InitializeReader(reader, [0x1, 0x0, 0x0, 0x0]);

		var key = reader.ReadMethodKey();

		Assert.Equal(1, key);
	}

	[Fact]
	public void IndicatesEndOfMessage()
	{
		using var reader = new RpcMessageReader(_bufferOptions);
		InitializeReader(reader, [0x0, 0x0, 0x0, 0x0]);

		Assert.False(reader.EndOfMessage);
		reader.ReadMethodKey();

		Assert.True(reader.EndOfMessage);
	}

	[Fact]
	public void FailsReadingParameterIfInsufficientData()
	{
		using var reader = new RpcMessageReader(_bufferOptions);
		InitializeReader(reader, [0x1, 0x0, 0x0, 0x0]);

		reader.ReadMethodKey();

		Assert.Throws<InvalidDataException>(reader.BeginReadParameter);
	}

	[Fact]
	public void FailsReadingParameterIfNegativeLength()
	{
		using var reader = new RpcMessageReader(_bufferOptions);
		InitializeReader(reader,
		[
			/* method */ 0x1, 0x0, 0x0, 0x0,
			/* parameter length */ 0xff, 0xff, 0xff, 0xff
		]);

		reader.ReadMethodKey();

		Assert.Throws<InvalidDataException>(reader.BeginReadParameter);
	}

	[Fact]
	public void FailsReadingParameterIfLengthExceedsMessage()
	{
		using var reader = new RpcMessageReader(_bufferOptions);
		InitializeReader(reader,
		[
			/* method */ 0x1, 0x0, 0x0, 0x0,
			/* parameter length */ 0x1, 0x0, 0x0, 0x0
		]);

		reader.ReadMethodKey();

		Assert.Throws<InvalidDataException>(reader.BeginReadParameter);
	}

	[Fact]
	public void ReadsParameterUsingSpan()
	{
		using var reader = new RpcMessageReader(_bufferOptions);
		InitializeReader(reader,
		[
			/* method */ 0x1, 0x0, 0x0, 0x0,
			/* parameter length */ 0x2, 0x0, 0x0, 0x0,
			/* parameter data */ 0xe8, 0xc2
		]);

		reader.ReadMethodKey();
		reader.BeginReadParameter();

		ReadOnlySpan<byte> expected = [0xe8, 0xc2];
		Assert.Equal(expected, reader.ParameterReader.ParameterSpan);
	}

	[Fact]
	public void ReadsParameterUsingStream()
	{
		using var reader = new RpcMessageReader(_bufferOptions);
		InitializeReader(reader,
		[
			/* method */ 0x1, 0x0, 0x0, 0x0,
			/* parameter length */ 0x2, 0x0, 0x0, 0x0,
			/* parameter data */ 0xe8, 0xc2
		]);

		reader.ReadMethodKey();
		reader.BeginReadParameter();

		Assert.Equal(0, reader.ParameterReader.ParameterStream.Position);
		Assert.Equal(2, reader.ParameterReader.ParameterStream.Length);

		var buffer = new byte[] { 0x0, 0x0, 0x0 };
		reader.ParameterReader.ParameterStream.ReadExactly(buffer, 0, 1);
		Assert.Equal(0xe8, buffer[0]);
		Assert.Equal(0x0, buffer[1]);
		Assert.Equal(0x0, buffer[2]);

		reader.ParameterReader.ParameterStream.ReadExactly(buffer, 1, 1);
		Assert.Equal(0xe8, buffer[0]);
		Assert.Equal(0xc2, buffer[1]);
		Assert.Equal(0x0, buffer[2]);

		Assert.Equal(reader.ParameterReader.ParameterStream.Length, reader.ParameterReader.ParameterStream.Position);
		Assert.Throws<EndOfStreamException>(() =>
		{
			reader.ParameterReader.ParameterStream.ReadExactly(buffer, 2, 1);
		});

		reader.EndReadParameter();
		Assert.True(reader.EndOfMessage);
	}

	[Fact]
	public void ReadsMultipleParameters()
	{
		using var reader = new RpcMessageReader(_bufferOptions);
		InitializeReader(reader,
		[
			/* method */ 0x1, 0x0, 0x0, 0x0,
			/* parameter length */ 0x1, 0x0, 0x0, 0x0,
			/* parameter data */ 0x1,
			/* parameter length */ 0x1, 0x0, 0x0, 0x0,
			/* parameter data */ 0x2
		]);

		reader.ReadMethodKey();

		reader.BeginReadParameter();
		ReadOnlySpan<byte> expected = [0x1];
		Assert.Equal(expected, reader.ParameterReader.ParameterSpan);
		reader.EndReadParameter();

		reader.BeginReadParameter();
		expected = [0x2];
		Assert.Equal(expected, reader.ParameterReader.ParameterSpan);
		reader.EndReadParameter();

		Assert.True(reader.EndOfMessage);
	}

	[Fact]
	public void ReadsMultipleMethods()
	{
		using var reader = new RpcMessageReader(_bufferOptions);
		InitializeReader(reader,
		[
			/* method */ 0x1, 0x0, 0x0, 0x0,
			/* parameter length */ 0x1, 0x0, 0x0, 0x0,
			/* parameter data */ 0x1,
			/* method */ 0x0, 0x1, 0x0, 0x0,
			/* parameter length */ 0x2, 0x0, 0x0, 0x0,
			/* parameter data */ 0x2, 0x3
		]);

		Assert.Equal(1, reader.ReadMethodKey());
		reader.BeginReadParameter();
		ReadOnlySpan<byte> expectedParameterData = [0x1];
		Assert.Equal(expectedParameterData, reader.ParameterReader.ParameterSpan);
		reader.EndReadParameter();

		Assert.Equal(256, reader.ReadMethodKey());
		reader.BeginReadParameter();
		expectedParameterData = [0x2, 0x3];
		Assert.Equal(expectedParameterData, reader.ParameterReader.ParameterSpan);
		reader.EndReadParameter();

		Assert.True(reader.EndOfMessage);
	}

	[Fact]
	public void AllowsReuseAfterReset()
	{
		using var reader = new RpcMessageReader(_bufferOptions);
		VerifyFunctionality();
		reader.Reset();
		VerifyFunctionality();

		void VerifyFunctionality()
		{
			InitializeReader(reader,
			[
				/* method */ 0x1, 0x0, 0x0, 0x0,
				/* parameter length */ 0x1, 0x0, 0x0, 0x0,
				/* parameter data */ 0x1,
				/* parameter length */ 0x1, 0x0, 0x0, 0x0,
				/* parameter data */ 0x2,
				/* method */ 0x2, 0x0, 0x0, 0x0,
				/* parameter length */ 0x1, 0x0, 0x0, 0x0,
				/* parameter data */ 0x3
			]);

			Assert.Equal(1, reader.ReadMethodKey());
			reader.BeginReadParameter();
			var buffer = new byte[1];
			reader.ParameterReader.ParameterStream.ReadExactly(buffer);
			Assert.Equal(1, buffer[0]);
			reader.EndReadParameter();
			reader.BeginReadParameter();
			reader.ParameterReader.ParameterStream.ReadExactly(buffer);
			Assert.Equal(2, buffer[0]);
			reader.EndReadParameter();
			Assert.Equal(2, reader.ReadMethodKey());
			reader.BeginReadParameter();
			reader.ParameterReader.ParameterStream.ReadExactly(buffer);
			Assert.Equal(3, buffer[0]);
			reader.EndReadParameter();
		}
	}

	private static void InitializeReader(RpcMessageReader reader, Span<byte> message)
	{
		var destination = reader.GetSpan(message.Length);
		message.CopyTo(destination);
		reader.Advance(message.Length);
	}
}
