using Nickogl.WebSockets.Rpc.LoadTest.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TestServer>();

var app = builder.Build();
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
app.UseRouting();
app.UseWebSockets();
app.Use(async (context, next) =>
{
	if (context.WebSockets.IsWebSocketRequest)
	{
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping, context.RequestAborted);
		using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
		var connection = new TestServerConnection(webSocket);
		var server = app.Services.GetRequiredService<TestServer>();
		await server.ProcessAsync(connection, cts.Token);
	}
	else
	{
		await next();
	}
});
app.Run();
