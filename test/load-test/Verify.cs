using Microsoft.Extensions.DependencyInjection;
using Nickogl.AspNetCore.IntegrationTesting;
using Nickogl.WebSockets.Rpc.LoadTest.App;

namespace Nickogl.WebSockets.Rpc.LoadTest;

internal static class Verify
{
	internal static void EqualPositions(WebApplicationTestHost<Program> app, TestHubTestClientCollection clients)
	{
		var serverPositions = app.Services.GetRequiredService<TestHub>().Positions;
		serverPositions.Sort();
		var clientPositions = clients.Select(c => c.Position).ToList();
		clientPositions.Sort();
		Assert.Equal(clientPositions, serverPositions);
	}
}
