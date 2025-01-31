using Nickogl.WebSockets.Rpc;

namespace SampleApp;

// HACK: Since we reference the generator project as a library in the integration tests, we have to generate the test client here
[WebSocketRpcTestClient<ChatServer>]
internal partial class ChatTestClient
{
	public ChatTestClient(Uri uri, TimeProvider? timeProvider = default, CancellationToken cancellationToken = default)
	{
		var serializer = new ChatTestClientSerializer();
		_serverSerializer = serializer;
		_clientSerializer = serializer;

		_timeProvider = timeProvider;

		ConnectAsync(uri, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
	}
}
