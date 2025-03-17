using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc.LoadTest.Server;

[RpcClient(RpcParameterSerialization.Specialized)]
public sealed partial class TestServerConnection
{
	private readonly static TestServerSerializer _serializer = new();

	protected override ITestServerConnectionSerializer Serializer => _serializer;

	public override WebSocket WebSocket { get; }
	public int Id { get; }

	public TestServerConnection(WebSocket webSocket)
	{
		WebSocket = webSocket;
		Id = GetHashCode();
	}

	[RpcMethod(1)]
	public partial ValueTask SetPosition(int x, int y, int z);
}
