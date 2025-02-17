using Nickogl.WebSockets.Rpc.Serialization;

namespace Nickogl.WebSockets.Rpc.UnitTest.Tests;

public class RpcMessageWriterTests : IDisposable
{
	private readonly DeterministicArrayPool<byte> _bufferPool;
	private readonly RpcMessageBufferOptions _bufferOptions;

	public RpcMessageWriterTests()
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
	public void WritesMethodKey()
	{
		using var writer = new RpcMessageWriter(_bufferOptions);

		writer.WriteMethodKey(256);

		ReadOnlySpan<byte> expected = [0x0, 0x1, 0x0, 0x0];
		Assert.Equal(expected, writer.WrittenSpan);
	}

	[Fact]
	public void WritesParameterUsingBufferWriter()
	{
		using var writer = new RpcMessageWriter(_bufferOptions);

		writer.WriteMethodKey(1);
		writer.BeginWriteParameter();
		var parameterSpan = writer.ParameterWriter.GetSpan(1);
		parameterSpan.Fill(2);
		writer.ParameterWriter.Advance(1);
		writer.EndWriteParameter();

		ReadOnlySpan<byte> expected = [0x1, 0x0, 0x0, 0x0, 0x1, 0x0, 0x0, 0x0, 0x2];
		Assert.Equal(expected, writer.WrittenSpan);
	}

	[Fact]
	public void WriteParameterUsingStream()
	{
		using var writer = new RpcMessageWriter(_bufferOptions);

		writer.WriteMethodKey(1);
		writer.BeginWriteParameter();
		writer.ParameterWriter.ParameterStream.WriteByte(2);
		writer.EndWriteParameter();

		ReadOnlySpan<byte> expected = [0x1, 0x0, 0x0, 0x0, 0x1, 0x0, 0x0, 0x0, 0x2];
		Assert.Equal(expected, writer.WrittenSpan);
	}

	[Fact]
	public void WriteMultipleParameters()
	{
		using var writer = new RpcMessageWriter(_bufferOptions);

		writer.WriteMethodKey(1);
		writer.BeginWriteParameter();
		writer.ParameterWriter.ParameterStream.WriteByte(2);
		writer.EndWriteParameter();
		writer.BeginWriteParameter();
		writer.ParameterWriter.ParameterStream.Write([0x3, 0x4]);
		writer.EndWriteParameter();

		ReadOnlySpan<byte> expected = [0x1, 0x0, 0x0, 0x0, 0x1, 0x0, 0x0, 0x0, 0x2, 0x2, 0x0, 0x0, 0x0, 0x3, 0x4];
		Assert.Equal(expected, writer.WrittenSpan);
	}

	[Fact]
	public void WriteMultipleMethods()
	{
		using var writer = new RpcMessageWriter(_bufferOptions);

		writer.WriteMethodKey(1);
		writer.BeginWriteParameter();
		writer.ParameterWriter.ParameterStream.WriteByte(2);
		writer.EndWriteParameter();
		writer.WriteMethodKey(2);
		writer.BeginWriteParameter();
		writer.ParameterWriter.ParameterStream.WriteByte(1);
		writer.EndWriteParameter();

		ReadOnlySpan<byte> expected = [0x1, 0x0, 0x0, 0x0, 0x1, 0x0, 0x0, 0x0, 0x2, 0x2, 0x0, 0x0, 0x0, 0x1, 0x0, 0x0, 0x0, 0x1];
		Assert.Equal(expected, writer.WrittenSpan);
	}

	[Fact]
	public void AllowsReuseAfterReset()
	{
		using var writer = new RpcMessageWriter(_bufferOptions);
		VerifyFunctionality();
		writer.Reset();
		VerifyFunctionality();

		void VerifyFunctionality()
		{
			writer.WriteMethodKey(1);
			writer.BeginWriteParameter();
			var parameterSpan = writer.ParameterWriter.GetSpan(1);
			parameterSpan.Fill(2);
			writer.ParameterWriter.Advance(1);
			writer.EndWriteParameter();
			writer.BeginWriteParameter();
			parameterSpan = writer.ParameterWriter.GetSpan(1);
			parameterSpan.Fill(3);
			writer.ParameterWriter.Advance(1);
			writer.EndWriteParameter();
			writer.WriteMethodKey(2);
			writer.BeginWriteParameter();
			parameterSpan = writer.ParameterWriter.GetSpan(2);
			parameterSpan.Fill(4);
			writer.ParameterWriter.Advance(2);
			writer.EndWriteParameter();

			ReadOnlySpan<byte> expected =
			[
				/* method */ 0x1, 0x0, 0x0, 0x0,
				/* parameter length */ 0x1, 0x0, 0x0, 0x0,
				/* parameter data */ 0x2,
				/* parameter length */ 0x1, 0x0, 0x0, 0x0,
				/* parameter data */ 0x3,
				/* method */ 0x2, 0x0, 0x0, 0x0,
				/* parameter length */ 0x2, 0x0, 0x0, 0x0,
				/* parameter data */ 0x4, 0x4,
			];
			Assert.Equal(expected, writer.WrittenSpan);
		}
	}
}
