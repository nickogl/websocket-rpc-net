using System.Net.WebSockets;

namespace SampleApp;

public partial class ChatServer : ChatServerBase
{
	public override ValueTask PostMessage(IChatClient client, string message) => throw new NotImplementedException();
	public override IChatClient CreateClient(WebSocket webSocket) => throw new NotImplementedException();
	protected override string DeserializeString(ReadOnlySpan<byte> data) => throw new NotImplementedException();
	protected override ReadOnlySpan<byte> SerializeString(string parameter) => throw new NotImplementedException();
}
