using Nickogl.WebSockets.Rpc;
using Nickogl.WebSockets.Rpc.Attributes;

namespace SampleApp;

public interface IChatClient : IWebSocketRpcClient
{
	string Name { get; set; }

	Task Kick(string reason);

	[WebSocketRpcMethod(1)]
	ValueTask PostMessage(string message);
}
