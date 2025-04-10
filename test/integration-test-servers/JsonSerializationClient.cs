using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc.IntegrationTest.Servers;

[RpcClient(RpcParameterSerialization.Json)]
public partial class JsonSerializationClient(WebSocket webSocket)
{
	public override WebSocket WebSocket { get; } = webSocket;

	[RpcMethod(1)]
	public partial ValueTask EmitEvent1(Event1 event1);

	[RpcMethod(2)]
	public partial ValueTask EmitEvent2(Event2 event2);
}
