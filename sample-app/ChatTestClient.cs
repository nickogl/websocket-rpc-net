using Nickogl.WebSockets.Rpc.Testing;

namespace SampleApp;

// HACK: Since we reference the generator project as a library in the integration tests, we have to generate the test client here
[RpcTestClient<ChatServer>]
internal partial class ChatTestClient
{
	private readonly ChatTestClientSerializer _serializer;
	private readonly TimeProvider? _timeProvider;

	protected override IChatServerTestSerializer ServerSerializer => _serializer;
	protected override IChatClientTestSerializer ClientSerializer => _serializer;
	protected override TimeProvider? TimeProvider => _timeProvider;
	protected override TimeSpan ReceiveTimeout => TimeSpan.FromSeconds(1);

	public ChatTestClient(Uri uri, TimeProvider? timeProvider = default, CancellationToken cancellationToken = default)
	{
		_serializer = new ChatTestClientSerializer();
		_timeProvider = timeProvider;

		ConnectAsync(uri, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
	}
}
