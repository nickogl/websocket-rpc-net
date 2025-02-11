using Nickogl.WebSockets.Rpc;

namespace SampleApp;

[WebSocketRpcClient(WebSocketRpcSerializationMode.Specialized)]
public sealed partial class ChatClient
{
	public ChatClient(IChatClientSerializer serializer)
	{
		_serializer = serializer;
	}

	[WebSocketRpcMethod(1)]
	public partial ValueTask PostMessage(string message);
}
