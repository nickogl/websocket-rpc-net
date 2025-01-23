using Nickogl.WebSockets.Rpc;

namespace SampleApp;

// HACK: Since we reference the generator project as a library in the integration tests, we have to generate the test client here
[WebSocketRpcTestClient<ChatServer>]
public partial class ChatTestClient
{
	public ChatTestClient(Uri uri, CancellationToken cancellationToken = default)
	{
		var serializer = new ChatTestClientSerializer();
		_serverSerializer = serializer;
		_clientSerializer = serializer;

		Connect(uri, cancellationToken);
	}
}
