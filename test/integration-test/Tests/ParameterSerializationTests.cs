using Nickogl.WebSockets.Rpc.IntegrationTest.Servers;
using System.Net;
using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc.IntegrationTest.Tests;

public class ParameterSerializationTests
{
	[Fact]
	public async Task SupportsGenericParameterSerialization()
	{
		var server = new GenericSerializationServer();
		var clientFactory = (WebSocket webSocket) => new GenericSerializationClient(webSocket);
		var testServer = new LocalWebSocketServer<GenericSerializationClient>(server, clientFactory);
		var testClient = new GenericSerializationTestClient();
		var event1 = new Event1() { Name = "event 1", Value = 42 };
		var event2 = new Event2() { Name = "event 2", Value = "test" };

		await testClient.ConnectAsync(testServer.BaseAddress);
		await testClient.EmitEvent1(event1);
		await testClient.EmitEvent2(event2);

		await testClient.Received.EmitEvent1(event1);
		await testClient.Received.EmitEvent2(event2);
	}

	[Fact]
	public async Task SupportsSpecializedParameterSerialization()
	{
		var server = new SpecializedSerializationServer();
		var clientFactory = (WebSocket webSocket) => new SpecializedSerializationClient(webSocket);
		var testServer = new LocalWebSocketServer<SpecializedSerializationClient>(server, clientFactory);
		var testClient = new SpecializedSerializationTestClient();
		var event1 = new Event1() { Name = "event 1", Value = 42 };
		var event2 = new Event2() { Name = "event 2", Value = "test" };

		await testClient.ConnectAsync(testServer.BaseAddress);
		await testClient.EmitEvent1(event1);
		await testClient.EmitEvent2(event2);

		await testClient.Received.EmitEvent1(event1);
		await testClient.Received.EmitEvent2(event2);
	}
}
