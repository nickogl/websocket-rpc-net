using Nickogl.WebSockets.Rpc.Testing;

namespace Nickogl.WebSockets.Rpc.IntegrationTest.Servers;

[RpcTestClient<GenericSerializationServer>]
public partial class GenericSerializationTestClient
{
	private static readonly GenericSerializationSerializer _serializer = new();

	protected override IGenericSerializationServerTestSerializer ServerSerializer => _serializer;
	protected override IGenericSerializationClientTestSerializer ClientSerializer => _serializer;
}