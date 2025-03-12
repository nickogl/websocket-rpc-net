using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc.IntegrationTest.Servers;

[RpcClient(RpcParameterSerialization.Generic)]
public partial class GenericSerializationClient(WebSocket webSocket)
{
	private readonly static GenericSerializationSerializer _serializer = new();

	public override WebSocket WebSocket { get; } = webSocket;

	protected override IGenericSerializationClientSerializer Serializer => _serializer;

	[RpcMethod(1)]
	public partial ValueTask EmitEvent1(Event1 event1);

	[RpcMethod(2)]
	public partial ValueTask EmitEvent2(Event2 event2);
}