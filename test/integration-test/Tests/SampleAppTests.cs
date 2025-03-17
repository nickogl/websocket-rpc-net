using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Nickogl.AspNetCore.IntegrationTesting;
using Nickogl.WebSockets.Rpc.Testing;
using SampleApp;

namespace Nickogl.WebSockets.Rpc.IntegrationTest.Tests;

public class SampleAppTests
{
	[Fact]
	public async Task BroadcastsChatMessage()
	{
		await using var app = new WebApplicationTestHost<Program>();
		await using var client1 = new ChatTestClient(app.BaseAddress);
		await using var client2 = new ChatTestClient(app.BaseAddress);
		await using var client3 = new ChatTestClient(app.BaseAddress);

		await client1.PostMessage("Hi!");

		await client1.Received.PostMessage("Hi!");
		await client2.Received.PostMessage("Hi!");
		await client3.Received.PostMessage("Hi!");
	}

#if !NET9_0_OR_GREATER
	[Fact]
	public async Task DisconnectsInactiveClients()
	{
		var timeProvider = new FakeTimeProvider();
		await using var app = new WebApplicationTestHost<Program>()
			.ConfigureServices(services =>
			{
				services.RemoveAll<TimeProvider>();
				services.AddSingleton<TimeProvider>(timeProvider);
			});
		await using var client = new ChatTestClient(app.BaseAddress, timeProvider);

		timeProvider.Advance(TimeSpan.FromSeconds(10));

		await client.Disconnected;
	}
#endif

	[Fact]
	public async Task SendsExistingMessagesUponConnecting()
	{
		await using var app = new WebApplicationTestHost<Program>();
		await using var client1 = new ChatTestClient(app.BaseAddress);
		await client1.PostMessage("Message 1");
		await client1.PostMessage("Message 2");
		await client1.Received.PostMessage(RpcParameter.Any<string>());
		await client1.Received.PostMessage(RpcParameter.Any<string>());

		await using var client2 = new ChatTestClient(app.BaseAddress);

		await client2.Received.PostMessage("Message 1");
		await client2.Received.PostMessage("Message 2");
	}
}
