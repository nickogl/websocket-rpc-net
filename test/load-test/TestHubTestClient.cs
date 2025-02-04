using Nickogl.WebSockets.Rpc.LoadTest.App;

namespace Nickogl.WebSockets.Rpc.LoadTest;

[WebSocketRpcTestClient<TestHub>]
internal partial class TestHubTestClient
{
	private readonly object _lock = new();
	private (int x, int y, int z) _position;

	public (int x, int y, int z) Position
	{
		get
		{
			lock (_lock)
			{
				return _position;
			}
		}
		set
		{
			lock (_lock)
			{
				_position = value;
			}
		}
	}

	public TestHubTestClient()
	{
		var serializer = new TestHubTestClientSerializer();
		_serverSerializer = serializer;
		_clientSerializer = serializer;
	}

	public (int x, int y, int z) NextPosition()
	{
		return Position = (Random.Shared.Next(), Random.Shared.Next(), Random.Shared.Next());
	}

	partial void OnSetPosition(int x, int y, int z)
	{
		Position = (x, y, z);
	}
}
