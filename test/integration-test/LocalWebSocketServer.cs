using Nickogl.WebSockets.Rpc.Internal;
using System.Net;
using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc.IntegrationTest;

public sealed class LocalWebSocketServer<T> : IDisposable where T : RpcClientBase
{
	private readonly CancellationTokenSource _cts = new();
	private readonly HttpListener _httpListener;
	private readonly int _port;

	public Uri BaseAddress => new UriBuilder(Uri.UriSchemeHttp, Dns.GetHostName(), _port).Uri;

	public LocalWebSocketServer(RpcServerBase<T> server, Func<WebSocket, T> clientFactory)
	{
		Exception? lastException = null;
		for (int port = IPEndPoint.MinPort; port <= IPEndPoint.MaxPort; port++)
		{
			try
			{
				_httpListener = new HttpListener();
				_httpListener.Prefixes.Add($"http://+:{port}/");
				_httpListener.Start();
				_port = port;

				_ = RunProcessingLoop(server, clientFactory, _httpListener!, _cts.Token);

				return;
			}
			catch (Exception e)
			{
				lastException = e;
			}
		}

		throw lastException!;
	}

	public void Dispose()
	{
		_cts.Cancel();
		_httpListener.Stop();
	}

	private static async Task RunProcessingLoop(
		RpcServerBase<T> server,
		Func<WebSocket, T> clientFactory,
		HttpListener httpListener,
		CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			var context = await httpListener.GetContextAsync();
			if (context.Request.IsWebSocketRequest)
			{
				var webSocketContext = await context.AcceptWebSocketAsync(null);
				_ = server.ProcessAsync(clientFactory(webSocketContext.WebSocket), cancellationToken);
			}
		}
	}
}