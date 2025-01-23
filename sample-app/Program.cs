using SampleApp;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;

// Integration tests cannot both reference the generator project as a library and analyzer, so we expose the marker attributes
[assembly: InternalsVisibleTo("integration-test")]

var builder = WebApplication.CreateBuilder(args);
builder.Services
	.AddSingleton<ChatSerializer>()
	.AddSingleton<IChatServerSerializer>(services => services.GetRequiredService<ChatSerializer>())
	.AddSingleton<IChatClientSerializer>(services => services.GetRequiredService<ChatSerializer>())
	.AddSingleton<ChatServer>()
	.AddTransient<ChatClient>();

var app = builder.Build();
app.UseRouting();
app.UseWebSockets();
app.Use(async (context, next) =>
{
	if (context.WebSockets.IsWebSocketRequest)
	{
		using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
		using var client = app.Services.GetRequiredService<ChatClient>();
		client.WebSocket = webSocket;

		var server = app.Services.GetRequiredService<ChatServer>();
		await server.ProcessAsync(client, context.RequestAborted);

		await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, context.RequestAborted);
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
