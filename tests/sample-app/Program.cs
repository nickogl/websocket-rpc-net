var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseHttpsRedirection();
app.MapGet("/connect", async (HttpContext httpContext, CancellationToken cancellationToken) =>
{
	await httpContext.WebSockets.AcceptWebSocketAsync();
});

app.Run();
