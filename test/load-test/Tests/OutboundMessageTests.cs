using Microsoft.Extensions.DependencyInjection;
using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Data;
using NBomber.Data.CSharp;
using Nickogl.AspNetCore.IntegrationTesting;
using Nickogl.WebSockets.Rpc.LoadTest.App;

namespace Nickogl.WebSockets.Rpc.LoadTest;

public class OutboundMessageTests
{
	[Fact]
	public async Task LowUserCountHighMessageCount()
	{
		await using var app = new WebApplicationTestHost<Program>();
		await using var clients = await TestHubTestClientCollection.CreateAsync(app.BaseAddress, 10);
		var dataFeed = DataFeed.Circular(app.Services.GetRequiredService<TestHub>().Connections);
		var scenario = Scenario
			.Create(nameof(OutboundMessageTests) + nameof(LowUserCountHighMessageCount), context => SendMessage(dataFeed, context))
			.WithoutWarmUp()
			.WithLoadSimulations(
				Simulation.Inject(
					rate: 50000,
					interval: TimeSpan.FromSeconds(1),
					during: TimeSpan.FromMinutes(1)));

		NBomberRunner
			.RegisterScenarios(scenario)
			.WithScenarioCompletionTimeout(TimeSpan.FromSeconds(10))
			.Run();

		Verify.EqualPositions(app, clients);
	}

	[Fact]
	public async Task HighUserCountLowMessageCount()
	{
		await using var app = new WebApplicationTestHost<Program>();
		await using var clients = await TestHubTestClientCollection.CreateAsync(app.BaseAddress, 10000);
		var dataFeed = DataFeed.Circular(app.Services.GetRequiredService<TestHub>().Connections);
		var scenario = Scenario
			.Create(nameof(OutboundMessageTests) + nameof(HighUserCountLowMessageCount), context => SendMessage(dataFeed, context))
			.WithoutWarmUp()
			.WithLoadSimulations(
				Simulation.Inject(
					rate: 10000,
					interval: TimeSpan.FromSeconds(1),
					during: TimeSpan.FromMinutes(1)));

		NBomberRunner
			.RegisterScenarios(scenario)
			.WithScenarioCompletionTimeout(TimeSpan.FromSeconds(10))
			.Run();

		Verify.EqualPositions(app, clients);
	}

	[Fact]
	public async Task MessageCountSpike()
	{
		await using var app = new WebApplicationTestHost<Program>();
		await using var clients = await TestHubTestClientCollection.CreateAsync(app.BaseAddress, 1000);
		var dataFeed = DataFeed.Circular(app.Services.GetRequiredService<TestHub>().Connections);
		var scenario = Scenario
			.Create(nameof(OutboundMessageTests) + nameof(MessageCountSpike), context => SendMessage(dataFeed, context))
			.WithoutWarmUp()
			.WithLoadSimulations(
				Simulation.Inject(
					rate: 1000,
					interval: TimeSpan.FromSeconds(1),
					during: TimeSpan.FromSeconds(20)),
				Simulation.Inject(
					rate: 100000,
					interval: TimeSpan.FromSeconds(1),
					during: TimeSpan.FromSeconds(20)),
				Simulation.Inject(
					rate: 1000,
					interval: TimeSpan.FromSeconds(1),
					during: TimeSpan.FromSeconds(20)));

		NBomberRunner
			.RegisterScenarios(scenario)
			.WithScenarioCompletionTimeout(TimeSpan.FromSeconds(10))
			.Run();

		Verify.EqualPositions(app, clients);
	}

	private static async Task<IResponse> SendMessage(IDataFeed<TestHubConnection> dataFeed, IScenarioContext context)
	{
		var client = dataFeed.GetNextItem(context.ScenarioInfo);
		var (x, y, z) = (Random.Shared.Next(), Random.Shared.Next(), Random.Shared.Next());
		lock (client)
		{
			client.X = x;
			client.Y = y;
			client.Z = z;
		}
		try
		{
			await client.SetPosition(x, y, z);
			return Response.Ok();
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			return Response.Fail(message: e.Message);
		}
	}
}
