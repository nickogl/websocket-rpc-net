using Nickogl.WebSockets.Rpc.LoadTest.App;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TestHub>();

var app = builder.Build();
app.UseRouting();
app.UseWebSockets();
app.Use(async (context, next) =>
{
	if (context.WebSockets.IsWebSocketRequest)
	{
		using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
		var connection = new TestHubConnection { WebSocket = webSocket };
		var hub = app.Services.GetRequiredService<TestHub>();
		await hub.ProcessAsync(connection, context.RequestAborted);
	}
	else
	{
		await next();
	}
});
app.Run();

public partial class Program
{
}
