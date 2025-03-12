using Nickogl.WebSockets.Rpc.Testing;

namespace Nickogl.WebSockets.Rpc.IntegrationTest.Servers;

[RpcTestClient<SpecializedSerializationServer>]
public partial class SpecializedSerializationTestClient
{
	private static readonly SpecializedSerializationSerializer _serializer = new();

	protected override ISpecializedSerializationServerTestSerializer ServerSerializer => _serializer;
	protected override ISpecializedSerializationClientTestSerializer ClientSerializer => _serializer;
}