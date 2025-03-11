using Nickogl.WebSockets.Rpc;
using System.Diagnostics;
using System.Net.WebSockets;

namespace SampleApp;

[RpcClient(RpcParameterSerialization.Specialized)]
internal sealed partial class ChatClient(IChatClientSerializer serializer)
{
	private WebSocket? _webSocket;

	public override WebSocket WebSocket
	{
		get { Debug.Assert(_webSocket != null); return _webSocket; }
	}

	public void SetWebSocket(WebSocket webSocket)
	{
		_webSocket = webSocket;
	}

	protected override IChatClientSerializer Serializer { get; } = serializer;

	[RpcMethod(1)]
	public partial ValueTask PostMessage(string message);
}
