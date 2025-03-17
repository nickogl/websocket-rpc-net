using Nickogl.WebSockets.Rpc.LoadTest.Server;
using Nickogl.WebSockets.Rpc.Testing;

namespace Nickogl.WebSockets.Rpc.LoadTest.Client;

[RpcTestClient<TestServer>]
internal partial class TestServerTestClient
{
	private readonly TestServerTestClientSerializer _serializer = new();
	private int _messagesReceived;

	protected override ITestServerTestSerializer ServerSerializer => _serializer;
	protected override ITestServerConnectionTestSerializer ClientSerializer => _serializer;
	protected override bool InterceptCalls => false;

	public int MessagesReceived => _messagesReceived;
	public bool Aborted { get; set; }

	partial void OnSetPosition(int x, int y, int z)
	{
		Interlocked.Increment(ref _messagesReceived);
	}
}
