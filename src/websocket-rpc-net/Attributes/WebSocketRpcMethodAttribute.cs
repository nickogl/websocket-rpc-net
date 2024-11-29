namespace Nickogl.WebSockets.Rpc.Attributes;

/// <summary>
/// Mark a method to be called by the websocket server or client. It must return
/// <c>void</c>, <c>ValueTask</c> or <c>Task</c> in a server and <c>ValueTask</c>
/// in a client. In a server, it also has to take an instance of the client as
/// the very first parameter.
/// </summary>
/// <param name="key">
/// Consistent, unique key of this method. Must be 1 or greater.
/// Changing this breaks existing clients.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WebSocketRpcMethodAttribute(int key) : Attribute
{
	/// <summary>Get the consistent, unique key of this method.</summary>
	public int Key { get; } = key;
}
