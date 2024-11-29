namespace Nickogl.WebSockets.Rpc.Attributes;

/// <summary>
/// Mark a type implementing <c>IWebSocketRpcServer</c> for source generation.
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class WebSocketRpcServerAttribute : Attribute
{
	/// <summary>How to generate methods for serializing and deserializing websocket-rpc parameters.</summary>
	public ParameterSerializationMode ParameterSerializationMode { get; set; }
}
