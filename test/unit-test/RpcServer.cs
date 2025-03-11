using Nickogl.WebSockets.Rpc.Internal;
using Nickogl.WebSockets.Rpc.Serialization;
using System.Buffers.Binary;
using System.Text;

namespace Nickogl.WebSockets.Rpc.UnitTest;

/// <summary>
/// An RPC server for testing that provides some RPC methods for use by the client.
/// </summary>
internal sealed class RpcServer : RpcServerBase<RpcClient>, IDisposable
{
	private readonly DeterministicArrayPool<byte> _pool = new();
	private readonly List<string> _receivedTexts = [];
	private readonly List<(int x, int y, int z)> _receivedCoordinates = [];

	public IReadOnlyCollection<string> ReceivedTexts => _receivedTexts;
	public IReadOnlyCollection<(int x, int y, int z)> ReceivedCoordinates => _receivedCoordinates;

	protected override TimeProvider? TimeProvider { get; }
	protected override TimeSpan? ClientTimeout { get; }

	public RpcServer(TimeProvider? timeProvider = null, TimeSpan? clientTimeout = null)
	{
		TimeProvider = timeProvider;
		ClientTimeout = clientTimeout;
	}

	public void Dispose()
	{
		_pool.Dispose();
	}

	protected override IRpcMessageReader GetMessageReader()
	{
		// Use DeterministicArrayPool to detect leaks at the end of a test
		return new RpcMessageReader(new() { Pool = _pool, MinimumSize = 1024, MaximumSize = Array.MaxLength });
	}

	protected override ValueTask DispatchAsync(RpcClient client, int methodKey, IRpcMessageReader messageReader)
	{
		if (methodKey == 1)
		{
			messageReader.BeginReadParameter();
			_receivedTexts.Add(Encoding.UTF8.GetString(messageReader.ParameterReader.Span));
			messageReader.EndReadParameter();
		}
		else if (methodKey == 2)
		{
			messageReader.BeginReadParameter();
			int x = BinaryPrimitives.ReadInt32LittleEndian(messageReader.ParameterReader.Span);
			messageReader.EndReadParameter();
			messageReader.BeginReadParameter();
			int y = BinaryPrimitives.ReadInt32LittleEndian(messageReader.ParameterReader.Span);
			messageReader.EndReadParameter();
			messageReader.BeginReadParameter();
			int z = BinaryPrimitives.ReadInt32LittleEndian(messageReader.ParameterReader.Span);
			messageReader.EndReadParameter();
			_receivedCoordinates.Add((x, y, z));
		}
		else
		{
			throw new InvalidDataException($"Invalid method key '{methodKey}'");
		}

		return ValueTask.CompletedTask;
	}
}
