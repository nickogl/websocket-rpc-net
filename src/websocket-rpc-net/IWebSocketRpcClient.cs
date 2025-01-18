using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Represents a websocket client to make remote procedure calls against.
/// </summary>
/// <remarks>Annotate available methods with <see cref="Attributes.WebSocketRpcMethodAttribute"/>.</remarks>
public interface IWebSocketRpcClient
{
	/// <summary>Get the underlying websocket of this client.</summary>
	WebSocket WebSocket { get; }
}
