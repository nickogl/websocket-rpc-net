using Nickogl.WebSockets.Rpc;

namespace SampleApp;

[WebSocketRpcServer<ChatClient>(WebSocketRpcSerializationMode.Specialized)]
internal sealed partial class ChatServer(IChatServerSerializer serializer, TimeProvider timeProvider)
{
	private readonly IChatServerSerializer _serializer = serializer;
	private readonly TimeProvider _timeProvider = timeProvider;
	private readonly List<string> _messages = [];
	private readonly List<ChatClient> _clients = [];

	protected override IChatServerSerializer Serializer => _serializer;
	protected override TimeProvider? TimeProvider => _timeProvider;
	protected override TimeSpan? ClientTimeout => TimeSpan.FromSeconds(5);
	protected override int MinimumMessageBufferSize => 1024;
	protected override int MaximumMessageBufferSize => 4096;

	public IReadOnlyCollection<string> Messages => _messages;

	protected override async ValueTask OnConnectedAsync(ChatClient client)
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
		await batch.SendAsync(client);
	}

	protected override ValueTask OnDisconnectedAsync(ChatClient client)
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
			await batch.SendAsync(targets);
		}
		catch (Exception)
		{
		}
	}
}
