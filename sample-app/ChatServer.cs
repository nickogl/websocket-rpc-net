using System.Net.WebSockets;
using System.Text;

namespace SampleApp;

public partial class ChatServer : ChatServerBase
{
	public List<string> Messages { get; } = [];

	public override ValueTask PostMessage(IChatClient client, string message)
	{
		Messages.Add(message);
		return ValueTask.CompletedTask;
	}

	public override IChatClient CreateClient(WebSocket webSocket)
	{
		return new ChatClient(this, webSocket);
	}

	protected override string DeserializeString(ReadOnlySpan<byte> data)
	{
		return Encoding.UTF8.GetString(data);
	}

	protected override ReadOnlySpan<byte> SerializeString(string parameter)
	{
		return Encoding.UTF8.GetBytes(parameter);
	}

	private sealed class ChatClient(ChatServerBase server, WebSocket webSocket) : ChatClientBase(server, webSocket)
	{
		public override string Name { get; set; } = null!;

		public override Task Kick(string reason)
		{
			return WebSocket.CloseAsync(WebSocketCloseStatus.Empty, reason, CancellationToken.None);
		}
	}
}
