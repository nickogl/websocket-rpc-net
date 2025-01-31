using Nickogl.WebSockets.Rpc;

namespace SampleApp;

[WebSocketRpcServer<ChatClient>(WebSocketRpcSerializationMode.Specialized)]
public sealed partial class ChatServer
{
	private readonly List<string> _messages = [];
	private readonly List<ChatClient> _clients = [];

	public IReadOnlyCollection<string> Messages => _messages;

	public ChatServer(IChatServerSerializer serializer, TimeProvider timeProvider)
	{
		_serializer = serializer;
		_clientTimeout = TimeSpan.FromSeconds(5);
		_timeProvider = timeProvider;
	}

	public void Dispose()
	{
	}

	private partial async ValueTask OnConnectedAsync(ChatClient client)
	{
		List<string> messageSnapshot;
		lock (_clients)
		{
			_clients.Add(client);
			messageSnapshot = [.. _messages];
		}

		using var batch = new ChatClient.Batch(client);
		foreach (var message in messageSnapshot)
		{
			batch.PostMessage(message);
		}
		await batch.FlushAsync(client);
	}

	private partial ValueTask OnDisconnectedAsync(ChatClient client)
	{
		lock (_clients)
		{
			_clients.Remove(client);
		}

		return ValueTask.CompletedTask;
	}

	[WebSocketRpcMethod(1)]
	public async ValueTask PostMessage(ChatClient client, string message)
	{
		List<ChatClient> targets;
		lock (_clients)
		{
			targets = [.. _clients];
			_messages.Add(message);
		}

		using var batch = new ChatClient.Batch(client);
		batch.PostMessage(message);
		try
		{
			await batch.FlushAsync(targets);
		}
		catch (Exception)
		{
		}
	}
}
