using SampleApp;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("integration-test")]

var builder = WebApplication.CreateBuilder(args);
builder.Services
	.AddSingleton(TimeProvider.System)
	.AddSingleton<ChatSerializer>()
	.AddSingleton<IChatServerSerializer>(services => services.GetRequiredService<ChatSerializer>())
	.AddSingleton<IChatClientSerializer>(services => services.GetRequiredService<ChatSerializer>())
	.AddSingleton<ChatServer>()
	.AddTransient<ChatClient>();

var app = builder.Build();
app.UseRouting();
app.UseWebSockets(new()
{
#if NET9_0_OR_GREATER
	KeepAliveTimeout = TimeSpan.FromSeconds(5),
#endif
});
app.Use(async (context, next) =>
{
	if (context.WebSockets.IsWebSocketRequest)
	{
		using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
		var client = app.Services.GetRequiredService<ChatClient>();
		client.SetWebSocket(webSocket);

		var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
		var server = app.Services.GetRequiredService<ChatServer>();
		await server.ProcessAsync(client, cts.Token);
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
