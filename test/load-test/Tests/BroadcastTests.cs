using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Data;
using NBomber.Data.CSharp;
using Nickogl.AspNetCore.IntegrationTesting;

namespace Nickogl.WebSockets.Rpc.LoadTest;

public class BroadcastTests
{
	[Fact]
	public async Task LowUserCountHighBroadcastCount()
	{
		await using var app = new WebApplicationTestHost<Program>();
		await using var clients = await TestHubTestClientCollection.CreateAsync(app.BaseAddress, 10);
		var dataFeed = DataFeed.Circular(clients);
		var scenario = Scenario
			.Create(nameof(BroadcastTests) + nameof(LowUserCountHighBroadcastCount), context => SendMessage(dataFeed, context))
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
	public async Task HighUserCountLowBroadcastCount()
	{
		await using var app = new WebApplicationTestHost<Program>();
		await using var clients = await TestHubTestClientCollection.CreateAsync(app.BaseAddress, 10000);
		var dataFeed = DataFeed.Circular(clients);
		var scenario = Scenario
			.Create(nameof(BroadcastTests) + nameof(HighUserCountLowBroadcastCount), context => SendMessage(dataFeed, context))
			.WithoutWarmUp()
			.WithLoadSimulations(
				Simulation.Inject(
					rate: 10,
					interval: TimeSpan.FromSeconds(1),
					during: TimeSpan.FromMinutes(1)));

		NBomberRunner
			.RegisterScenarios(scenario)
			.WithScenarioCompletionTimeout(TimeSpan.FromSeconds(10))
			.Run();

		Verify.EqualPositions(app, clients);
	}

	[Fact]
	public async Task BroadcastCountSpike()
	{
		await using var app = new WebApplicationTestHost<Program>();
		await using var clients = await TestHubTestClientCollection.CreateAsync(app.BaseAddress, 1000);
		var dataFeed = DataFeed.Circular(clients);
		var scenario = Scenario
			.Create(nameof(BroadcastTests) + nameof(BroadcastCountSpike), context => SendMessage(dataFeed, context))
			.WithoutWarmUp()
			.WithLoadSimulations(
				Simulation.Inject(
					rate: 100,
					interval: TimeSpan.FromSeconds(1),
					during: TimeSpan.FromSeconds(20)),
				Simulation.Inject(
					rate: 10000,
					interval: TimeSpan.FromSeconds(1),
					during: TimeSpan.FromSeconds(20)),
				Simulation.Inject(
					rate: 100,
					interval: TimeSpan.FromSeconds(1),
					during: TimeSpan.FromSeconds(20)));

		NBomberRunner
			.RegisterScenarios(scenario)
			.WithScenarioCompletionTimeout(TimeSpan.FromSeconds(10))
			.Run();

		Verify.EqualPositions(app, clients);
	}

	private static async Task<IResponse> SendMessage(IDataFeed<TestHubTestClient> dataFeed, IScenarioContext context)
	{
		var client = dataFeed.GetNextItem(context.ScenarioInfo);
		var (x, y, z) = (Random.Shared.Next(), Random.Shared.Next(), Random.Shared.Next());
		try
		{
			await client.BroadcastPosition(x, y, z);
			return Response.Ok();
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			return Response.Fail(message: e.Message);
		}
	}
}
