using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nickogl.WebSockets.Rpc.Internal;
using System.Net;
using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc.IntegrationTest;

public sealed class LocalWebSocketServer<T> : IAsyncDisposable where T : RpcClientBase
{
	private readonly CancellationTokenSource _cts = new();
	private readonly WebApplication _app;
	private readonly int _port;

	public Uri BaseAddress => new UriBuilder(Uri.UriSchemeHttp, Dns.GetHostName(), _port).Uri;

	public LocalWebSocketServer(RpcServerBase<T> server, Func<WebSocket, T> clientFactory)
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseKestrel(options => options.ListenAnyIP(0));
		builder.Services.AddWebSockets(options => { });

		_app = builder.Build();
		_app.UseRouting();
		_app.UseWebSockets();
		_app.Use(async (context, next) =>
		{
			if (context.WebSockets.IsWebSocketRequest)
			{
				using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
				var client = clientFactory(webSocket);
				await server.ProcessAsync(client, _cts.Token);
			}
			else
			{
				await next();
			}
		});
		_app.Start();

		_port = new Uri(_app.Services.GetRequiredService<IServer>()
				.Features.Get<IServerAddressesFeature>()!
				.Addresses.First())
			.Port;
	}

	public ValueTask DisposeAsync()
	{
		_cts.Cancel();
		return _app.DisposeAsync();
	}
}
