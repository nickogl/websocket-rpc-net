namespace Nickogl.WebSockets.Rpc.LoadTest;

internal sealed class TestHubTestClientCollection : List<TestHubTestClient>, IAsyncDisposable
{
	private readonly bool _graduallyDisconnect;

	public TestHubTestClientCollection(IEnumerable<TestHubTestClient>? clients = null, bool graduallyDisconnect = true)
		: base(clients ?? [])
	{
		_graduallyDisconnect = graduallyDisconnect;
	}

	public async ValueTask DisposeAsync()
	{
		if (_graduallyDisconnect)
		{
			foreach (var client in this)
			{
				await client.DisposeAsync();
			}
		}
		else
		{
			await Task.WhenAll(this.Select(c => c.DisposeAsync().AsTask()));
		}
	}

	public static async Task<TestHubTestClientCollection> CreateAsync(Uri uri, int count, bool graduallyConnect = true)
	{
		var clients = new List<TestHubTestClient>(capacity: count);
		for (int i = 0; i < count; i++)
		{
			clients.Add(new TestHubTestClient());
		}

		if (graduallyConnect)
		{
			foreach (var client in clients)
			{
				await client.ConnectAsync(uri);
			}
		}
		else
		{
			await Task.WhenAll(clients.Select(c => c.ConnectAsync(uri)));
		}

		return new TestHubTestClientCollection(clients, graduallyConnect);
	}
}
