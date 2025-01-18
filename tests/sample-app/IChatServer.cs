using Nickogl.WebSockets.Rpc;
using Nickogl.WebSockets.Rpc.Attributes;

namespace SampleApp;

[WebSocketRpcServer(ParameterSerializationMode = ParameterSerializationMode.Specialized)]
public interface IChatServer : IWebSocketRpcServer<IChatClient>
{
	[WebSocketRpcMethod(1)]
	ValueTask PostMessage(IChatClient client, string message);
}
