using Microsoft.Extensions.Time.Testing;
using System.Net.WebSockets;
using System.Text;

namespace Nickogl.WebSockets.Rpc.UnitTest.Tests;

public class RpcServerBaseTests
{
	[Fact]
	public async Task RejectsMessagesOfTypeText()
	{
		using var server = new RpcServer();
		using var client = new RpcClient();

		var processTask = server.ProcessAsync(client, default);
		await client.WebSocket.ReceiveMessageSegmentAsync(Encoding.UTF8.GetBytes("test"), WebSocketMessageType.Text, endOfMessage: true);

		await Assert.ThrowsAsync<InvalidDataException>(() => processTask);
	}

#if !NET9_0_OR_GREATER
	[Fact]
	public async Task RejectsPingMessagesIfTimeoutDisabled()
	{
		using var server = new RpcServer(clientTimeout: null);
		using var client = new RpcClient();

		var processTask = server.ProcessAsync(client, default);
		await client.WebSocket.ReceiveMessageSegmentAsync([0x0, 0x0, 0x0, 0x0], WebSocketMessageType.Binary, endOfMessage: true);

		await Assert.ThrowsAsync<InvalidDataException>(() => processTask);
	}
#endif

	[Fact]
	public async Task StopsProcessingClientUponCancellation()
	{
		using var server = new RpcServer();
		using var client = new RpcClient();
		using var cts = new CancellationTokenSource();

		var processTask = server.ProcessAsync(client, cts.Token);
		cts.Cancel();

		await Assert.ThrowsAsync<OperationCanceledException>(() => processTask);
		Assert.Equal(WebSocketState.Aborted, client.WebSocket.State);
	}

	[Fact]
	public async Task StopsProcessingClientUponClosure()
	{
		using var server = new RpcServer();
		using var client = new RpcClient();

		await Task.WhenAll(
			server.ProcessAsync(client, default),
			client.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null));

		Assert.Equal(WebSocketState.Closed, client.WebSocket.State);
		Assert.Equal(WebSocketCloseStatus.NormalClosure, client.WebSocket.CloseStatus);
		Assert.Equal(WebSocketMessageType.Close, client.WebSocket.SentMessageSegments.Last().Type);
	}

#if !NET9_0_OR_GREATER
	[Fact]
	public async Task StopsProcessingClientUponTimeout()
	{
		var timeProvider = new FakeTimeProvider();
		using var server = new RpcServer(timeProvider, clientTimeout: TimeSpan.FromSeconds(5));
		using var client = new RpcClient();

		var processTask = server.ProcessAsync(client, default);
		timeProvider.Advance(TimeSpan.FromSeconds(6));

		var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => processTask);
		Assert.Equal(WebSocketState.Aborted, client.WebSocket.State);
	}
#endif

	[Fact]
	public async Task StopsProcessingClientUponReceivingInvalidMessage()
	{
		using var server = new RpcServer();
		using var client = new RpcClient();

		var processTask = server.ProcessAsync(client, default);
		await client.WebSocket.ReceiveMessageSegmentAsync([0x3, 0x0, 0x0, 0x0], WebSocketMessageType.Binary, endOfMessage: true);

		await Assert.ThrowsAsync<InvalidDataException>(() => processTask);
	}

#if !NET9_0_OR_GREATER
	[Fact]
	public async Task ResetsClientTimeoutUponPing()
	{
		var timeProvider = new FakeTimeProvider();
		using var server = new RpcServer(timeProvider, clientTimeout: TimeSpan.FromSeconds(5));
		using var client = new RpcClient();

		var processTask = server.ProcessAsync(client, default);
		timeProvider.Advance(TimeSpan.FromSeconds(3));
		// Send two pings to avoid a race condition that could potentially trigger the timeout
		await client.WebSocket.ReceiveMessageSegmentAsync([0x0, 0x0, 0x0, 0x0], WebSocketMessageType.Binary, endOfMessage: true);
		await client.WebSocket.ReceiveMessageSegmentAsync([0x0, 0x0, 0x0, 0x0], WebSocketMessageType.Binary, endOfMessage: true);
		timeProvider.Advance(TimeSpan.FromSeconds(3));
		await client.WebSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, null);

		// Should not throw an exception
		await processTask;
		// Ensure that the process was not terminated by the timeout (which results in WebSocketCloseStatus.NormalClosure)
		Assert.Equal(WebSocketCloseStatus.InternalServerError, client.WebSocket.CloseStatus);
	}
#endif

	[Fact]
	public async Task ProcessesRemoteProcedureCalls()
	{
		using var server = new RpcServer();
		using var client = new RpcClient();
		var message = new byte[]
		{
			/* method key */ 0x2, 0x0, 0x0, 0x0,
			/* parameter 1 */ 0x4, 0x0, 0x0, 0x0, 0x8, 0x0, 0x0, 0x0,
			/* parameter 2 */ 0x4, 0x0, 0x0, 0x0, 0x4, 0x0, 0x0, 0x0,
			/* parameter 3 */ 0x4, 0x0, 0x0, 0x0, 0x2, 0x0, 0x0, 0x0,

			/* method key */ 0x1, 0x0, 0x0, 0x0,
			/* parameter 1 */ 0x4, 0x0, 0x0, 0x0, 0x74, 0x65, 0x73, 0x74
		};

		await Task.WhenAll(
			server.ProcessAsync(client, default),
			client.WebSocket.ReceiveMessageSegmentAsync(message, WebSocketMessageType.Binary, endOfMessage: true),
			client.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null));

		Assert.Single(server.ReceivedTexts, "test");
		Assert.Single(server.ReceivedCoordinates, (8, 4, 2));
	}
}
