namespace Nickogl.WebSockets.Rpc.IntegrationTest.Servers;

[RpcServer<SpecializedSerializationClient>(RpcParameterSerialization.Specialized)]
public partial class SpecializedSerializationServer
{
	private readonly static SpecializedSerializationSerializer _serializer = new();

	protected override ISpecializedSerializationServerSerializer Serializer => _serializer;

	[RpcMethod(1)]
	public ValueTask EmitEvent1(SpecializedSerializationClient client, Event1 event1)
	{
		return client.EmitEvent1(event1);
	}

	[RpcMethod(2)]
	public ValueTask EmitEvent2(SpecializedSerializationClient client, Event2 event2)
	{
		return client.EmitEvent2(event2);
	}
}