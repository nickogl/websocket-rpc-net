using Nickogl.WebSockets.Rpc;

namespace SampleApp;

[WebSocketRpcServer<ChatClient>(WebSocketRpcSerializationMode.Specialized)]
public sealed partial class ChatServer
{
	public ChatServer(IChatServerSerializer serializer)
	{
		_serializer = serializer;
	}

	public void Dispose()
	{
	}

	[WebSocketRpcMethod(1)]
	public ValueTask PostMessage(ChatClient client, string message)
	{
		return ValueTask.CompletedTask;
	}
}
