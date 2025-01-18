using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Represents a websocket server to receive remote procedure calls from clients.
/// </summary>
/// <remarks>Annotate available methods with <see cref="Attributes.WebSocketRpcMethodAttribute"/>.</remarks>
/// <typeparam name="T">Type of the corresponding websocket-rpc client.</typeparam>
public interface IWebSocketRpcServer<T> where T : IWebSocketRpcClient
{
	/// <summary>
	/// Create a new client for use with this server. It can be augmented with custom
	/// state before running its lifecycle with <see cref="ProcessAsync"/>.
	/// </summary>
	/// <param name="webSocket">Websocket to use for the client.</param>
	/// <returns>A client instance to use with this server.</returns>
	T CreateClient(WebSocket webSocket);

	/// <summary>
	/// Process a client's websocket messages until it disconnects or the provided
	/// <see cref="cancellationToken"/> is cancelled.
	/// </summary>
	/// <param name="client">Client whose websocket messages to process.</param>
	/// <param name="cancellationToken">Cancellation token to stop processing messages.</param>
	/// <returns>A task that represents the lifecycle of the provided client.</returns>
	/// <exception cref="ArgumentException">The <paramref name="client"/> was not created for this server.</exception>
	/// <exception cref="WebSocketException">An operation on the client's websocket failed.</exception>
	/// <exception cref="WebSocketRpcMessageException">Invalid websocket-rpc message encountered.</exception>
	Task ProcessAsync(T client, CancellationToken cancellationToken);
}
