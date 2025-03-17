namespace Nickogl.WebSockets.Rpc.LoadTest.Client;

internal sealed class TestServerTestClientCollection : List<TestServerTestClient>, IAsyncDisposable
{
	private readonly int _maximumConcurrency;

	public TestServerTestClientCollection(IEnumerable<TestServerTestClient>? clients = null, int maximumConcurrency = 1)
		: base(clients ?? [])
	{
		_maximumConcurrency = maximumConcurrency;
	}

	public async ValueTask DisposeAsync()
	{
		await Parallel.ForEachAsync(
			this,
			new ParallelOptions() { MaxDegreeOfParallelism = _maximumConcurrency },
			async (client, cancellationToken) =>
			{
				try
				{
					await client.DisconnectAsync(cancellationToken);
				}
				catch (Exception)
				{
				}
			});
	}

	public static async Task<TestServerTestClientCollection> CreateAsync(Uri uri, int count, int maximumConcurrency = 1, CancellationToken cancellationToken = default)
	{
		var clients = new List<TestServerTestClient>(capacity: count);
		for (int i = 0; i < count; i++)
		{
			clients.Add(new TestServerTestClient());
		}

		await Parallel.ForEachAsync(
			clients,
			new ParallelOptions() { CancellationToken = cancellationToken, MaxDegreeOfParallelism = maximumConcurrency },
			async (client, cancellationToken) => await client.ConnectAsync(uri, cancellationToken));

		return new TestServerTestClientCollection(clients, maximumConcurrency);
	}
}
