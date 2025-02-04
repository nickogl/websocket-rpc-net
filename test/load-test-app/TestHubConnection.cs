namespace Nickogl.WebSockets.Rpc.LoadTest.App;

[WebSocketRpcClient(WebSocketRpcSerializationMode.Specialized)]
public sealed partial class TestHubConnection
{
	public int Id { get; }
	public int X { get; set; }
	public int Y { get; set; }
	public int Z { get; set; }

	public TestHubConnection()
	{
		_serializer = new TestHubSerializer();
		Id = GetHashCode();
	}

	[WebSocketRpcMethod(1)]
	public partial ValueTask SetPosition([Disposable<PooledByteArray>] int x, [Disposable<PooledByteArray>] int y, [Disposable<PooledByteArray>] int z);
}
