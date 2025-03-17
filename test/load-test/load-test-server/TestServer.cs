namespace Nickogl.WebSockets.Rpc.LoadTest.Server;

[RpcServer<TestServerConnection>(RpcParameterSerialization.Specialized)]
public sealed partial class TestServer
{
	private readonly static TestServerSerializer _serializer = new();
	private readonly ConnectionComparer _connectionComparer = new();
	private readonly List<TestServerConnection> _connections = [];

	protected override ITestServerSerializer Serializer => _serializer;

	public IEnumerable<TestServerConnection> Connections => _connections;

	[RpcMethod(1)]
	public ValueTask SetPosition(TestServerConnection client, int x, int y, int z)
	{
		return client.SetPosition(x, y, z);
	}

	protected override ValueTask OnConnectedAsync(TestServerConnection client)
	{
		lock (_connections)
		{
			var index = _connections.BinarySearch(client, _connectionComparer);
			if (index < 0)
			{
				_connections.Insert(~index, client);
			}
		}
		return ValueTask.CompletedTask;
	}

	protected override ValueTask OnDisconnectedAsync(TestServerConnection client)
	{
		lock (_connections)
		{
			var index = _connections.BinarySearch(client, _connectionComparer);
			if (index >= 0)
			{
				_connections.RemoveAt(index);
			}
		}
		return ValueTask.CompletedTask;
	}

	private sealed class ConnectionComparer : IComparer<TestServerConnection>
	{
		public int Compare(TestServerConnection? x, TestServerConnection? y)
		{
			if (x is null && y is null)
			{
				return 0;
			}
			if (x is null)
			{
				return -1;
			}
			if (y is null)
			{
				return 1;
			}

			return x.Id.CompareTo(y.Id);
		}
	}
}
