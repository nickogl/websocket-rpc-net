namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Represents errors that are raised when encountering invalid websocket-rpc messages.
/// </summary>
public sealed class WebSocketRpcMessageException : Exception
{
	public WebSocketRpcMessageException(string? message) : base(message)
	{
	}

	public WebSocketRpcMessageException(string? message, Exception? innerException) : base(message, innerException)
	{
	}
}
