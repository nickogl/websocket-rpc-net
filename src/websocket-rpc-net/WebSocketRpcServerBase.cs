using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Base websocket server used by the generated code. Do not directly inherit from this.
/// </summary>
/// <typeparam name="T">Type of the corresponding websocket-rpc client.</typeparam>
public abstract class WebSocketRpcServerBase<T> where T : IWebSocketRpcClient
{
	/// <summary>
	/// Called when the server has received a websocket-rpc message from a client.
	/// </summary>
	/// <param name="message">Raw message data sent by the client.</param>
	/// <exception cref="WebSocketRpcMessageException">Client sent an invalid websocket-rpc message.</exception>
	protected abstract ValueTask OnMessageAsync(T client, Span<byte> message);

	/// <summary>
	/// Called when a client has connected to the server.
	/// </summary>
	/// <param name="client">Client who has connected.</param>
	protected virtual ValueTask OnConnectAsync(T client) => ValueTask.CompletedTask;

	/// <summary>
	/// Called when a client is about to disconnect from the server.
	/// </summary>
	/// <param name="client">Client who is about to disconnect.</param>
	protected virtual ValueTask OnDisconnectAsync(T client) => ValueTask.CompletedTask;

	/// <summary>
	/// Create a new client for use with this server. It can be augmented with custom
	/// state before running its lifecycle with <see cref="ProcessAsync"/>.
	/// </summary>
	/// <param name="webSocket">Websocket to use for the client.</param>
	/// <returns>A client instance to use with this server.</returns>
	public abstract T CreateClient(WebSocket webSocket);

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
	/// <remarks>Override this to provide your own message processing loop.</remarks>
	public virtual Task ProcessAsync(T client, CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
