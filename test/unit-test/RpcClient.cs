using Nickogl.WebSockets.Rpc.Internal;

namespace Nickogl.WebSockets.Rpc.UnitTest;

/// <summary>
/// An RPC client for testing that provides some RPC methods for use by the server.
/// </summary>
internal sealed class RpcClient : RpcClientBase, IDisposable
{
	public override FakeWebSocket WebSocket { get; } = new();

	public void Dispose()
	{
		WebSocket.Dispose();
	}
}
