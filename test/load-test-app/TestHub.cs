namespace Nickogl.WebSockets.Rpc.LoadTest.App;

[WebSocketRpcServer<TestHubConnection>(WebSocketRpcSerializationMode.Specialized)]
public sealed partial class TestHub
{
	private readonly ConnectionComparer _connectionComparer = new();
	private readonly List<TestHubConnection> _connections = [];

	public IEnumerable<TestHubConnection> Connections => _connections;

	public List<(int x, int y, int z)> Positions
	{
		get
		{
			lock (_connections)
			{
				var result = new List<(int x, int y, int z)>(capacity: _connections.Count);
				result.AddRange(_connections.Select(c => (c.X, c.Y, c.Z)));
				return result;
			}
		}
	}

	public TestHub()
	{
		_serializer = new TestHubSerializer();
	}

	[WebSocketRpcMethod(1)]
	public ValueTask SetPosition(TestHubConnection client, int x, int y, int z)
	{
		client.X = x;
		client.Y = y;
		client.Z = z;
		return ValueTask.CompletedTask;
	}

	[WebSocketRpcMethod(2)]
	public async ValueTask BroadcastPosition(TestHubConnection client, int x, int y, int z)
	{
		List<TestHubConnection> connections;
		lock (_connections)
		{
			connections = [.. _connections];
		}

		using var batch = new TestHubConnection.Batch(client);
		batch.SetPosition(x, y, z);
		foreach (var conn in connections)
		{
			conn.X = x;
			conn.Y = y;
			conn.Z = z;
			await batch.SendAsync(conn);
		}
	}

	private partial ValueTask OnConnectedAsync(TestHubConnection client)
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

	private partial ValueTask OnDisconnectedAsync(TestHubConnection client)
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

	private sealed class ConnectionComparer : IComparer<TestHubConnection>
	{
		public int Compare(TestHubConnection? x, TestHubConnection? y)
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
