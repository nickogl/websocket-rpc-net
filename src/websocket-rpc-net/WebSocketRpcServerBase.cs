using System.Buffers;
using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Base websocket server used by the generated code. Do not directly inherit from this.
/// </summary>
/// <typeparam name="T">Type of the corresponding websocket-rpc client.</typeparam>
public abstract class WebSocketRpcServerBase<T> where T : IWebSocketRpcClient
{
	/// <summary>Time after which connections are closed if they fail to acknowledge ping frames.</summary>
	protected virtual TimeSpan? ConnectionTimeout { get; }

	/// <summary>Array pool to use for allocating buffers.</summary>
	protected virtual ArrayPool<byte> Allocator { get; } = ArrayPool<byte>.Shared;

	/// <summary>Size of the message buffer in bytes. Defaults to 8 KiB.</summary>
	/// <remarks>
	/// Choose one that the vast majority of messages will fit into.
	/// If a message does not fit, the buffer grows exponentially until <see cref="MaximumMessageSize"/> is reached.
	/// If you are unsure, choose a generous value first and then consult the recorded metrics to refine it.
	/// </remarks>
	protected virtual int MessageBufferSize { get; } = 1024 * 8;

	/// <summary>Maximum size of messages. Defaults to 64 KiB.</summary>
	/// <remarks>
	/// Choose one that all legit messages will fit into.
	/// If you are unsure, choose a generous value first and then consult the recorded metrics to refine it.
	/// </remarks>
	protected virtual int MaximumMessageSize { get; } = 1024 * 64;

	/// <summary>Maximum size of parameters. Defaults to 4 KiB.</summary>
	/// <remarks>
	/// Choose one that all parameters of legit messages will fit into.
	/// If you are unsure, choose a generous value first and then consult the recorded metrics to refine it.
	/// </remarks>
	protected virtual int MaximumParameterSize { get; } = 1024 * 4;

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
}
