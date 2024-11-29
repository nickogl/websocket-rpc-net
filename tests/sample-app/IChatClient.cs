using Nickogl.WebSockets.Rpc;
using Nickogl.WebSockets.Rpc.Attributes;

namespace SampleApp;

public interface IChatClient : IWebSocketRpcClient
{
	[WebSocketRpcMethod(1)]
	ValueTask PostMessage(string message);
}
