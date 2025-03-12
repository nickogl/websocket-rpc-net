namespace Nickogl.WebSockets.Rpc.IntegrationTest.Servers;

[RpcServer<GenericSerializationClient>(RpcParameterSerialization.Generic)]
public partial class GenericSerializationServer
{
	private readonly static GenericSerializationSerializer _serializer = new();

	protected override IGenericSerializationServerSerializer Serializer => _serializer;

	[RpcMethod(1)]
	public ValueTask EmitEvent1(GenericSerializationClient client, Event1 event1)
	{
		return client.EmitEvent1(event1);
	}

	[RpcMethod(2)]
	public ValueTask EmitEvent2(GenericSerializationClient client, Event2 event2)
	{
		return client.EmitEvent2(event2);
	}
}