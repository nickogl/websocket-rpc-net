using Nickogl.WebSockets.Rpc;
using Nickogl.WebSockets.Rpc.Serialization;
using System.Buffers;

namespace SampleApp;

[RpcServer<ChatClient>(RpcParameterSerialization.Specialized)]
internal sealed partial class ChatServer
{
	private readonly List<string> _messages = [];
	private readonly List<ChatClient> _clients = [];

	protected override IChatServerSerializer Serializer { get; }
#if !NET9_0_OR_GREATER
	protected override TimeProvider? TimeProvider { get; }
	protected override TimeSpan? ClientTimeout => TimeSpan.FromSeconds(5);
#endif

#if NET9_0_OR_GREATER
	public ChatServer(IChatServerSerializer serializer)
	{
		Serializer = serializer;
	}
#else
	public ChatServer(IChatServerSerializer serializer, TimeProvider timeProvider)
	{
		Serializer = serializer;
		TimeProvider = timeProvider;
	}
#endif

	public IReadOnlyCollection<string> Messages => _messages;

	protected override async ValueTask OnConnectedAsync(ChatClient client)
	{
		List<string> messageSnapshot;
		lock (_clients)
		{
			_clients.Add(client);
			messageSnapshot = [.. _messages];
		}

		if (messageSnapshot.Count > 0)
		{
			using var messageWriter = new RpcMessageWriter(new()
			{
				Pool = ArrayPool<byte>.Shared,
				MinimumSize = 1024,
				MaximumSize = 1024 * 64,
			});
			foreach (var message in messageSnapshot)
			{
				client.PostMessage(messageWriter, message);
			}
			await client.SendAsync(messageWriter);
		}
	}

	protected override ValueTask OnDisconnectedAsync(ChatClient client)
	{
		lock (_clients)
		{
			_clients.Remove(client);
		}

		return ValueTask.CompletedTask;
	}

	[RpcMethod(1)]
	public async ValueTask PostMessage(ChatClient client, string message)
	{
		if (string.IsNullOrEmpty(message))
		{
			return;
		}

		List<ChatClient> targets;
		lock (_clients)
		{
			targets = [.. _clients];
			_messages.Add(message);
		}

		if (targets.Count > 0)
		{
			using var messageWriter = new RpcMessageWriter(new()
			{
				Pool = ArrayPool<byte>.Shared,
				MinimumSize = 1024,
				MaximumSize = 1024 * 16,
			});
			client.PostMessage(messageWriter, message);
			foreach (var target in targets)
			{
				await target.TrySendAsync(messageWriter);
			}
		}
	}
}
