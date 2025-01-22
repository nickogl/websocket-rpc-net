using Microsoft.CodeAnalysis;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	private static void GenerateAttributes(IncrementalGeneratorPostInitializationContext context)
	{
		context.AddSource("WebSocketRpcMethodAttribute.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_ATTRIBUTES
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Mark a method to be called by the websocket server or client. In a server
/// context, it has to additionally take the client as its first parameter. In
/// all contexts it must return a ValueTask.
/// </summary>
/// <param name=""key"">
/// Consistent, unique key of this method. Must be 1 or greater.
/// Changing this value on a method breaks existing clients.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class WebSocketRpcMethodAttribute(int key) : Attribute
{{
	/// <summary>Get the consistent, unique key of this method.</summary>
	public int Key {{ get; }} = key;
}}
#endif");

		context.AddSource("WebSocketRpcSerializationMode.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_ATTRIBUTES
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Serialization modes for RPC parameters.
/// </summary>
internal enum WebSocketRpcSerializationMode
{{
	/// <summary>Use generic serialization and deserialization methods for all types of parameters.</summary>
	Generic,

	/// <summary>Use specialized serialization and deserialization methods for each type referenced in parameters.</summary>
	Specialized,
}}
#endif");

		context.AddSource("WebSocketRpcClientAttribute.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_ATTRIBUTES
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Generate a websocket-rpc client implementation in a class. The class must
/// be partial and implement some methods for the generated code. It also has
/// to annotate all methods callable by the server with <see cref=""WebSocketRpcMethodAttribute""/>.
/// </summary>
/// <param name=""serializationMode"">Serialization mode to use for RPC parameters.</param>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class WebSocketRpcClientAttribute(WebSocketRpcSerializationMode serializationMode) : Attribute
{{
	public WebSocketRpcSerializationMode SerializationMode {{ get; }} = serializationMode;
}}
#endif");

		context.AddSource("WebSocketRpcServerAttribute.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_ATTRIBUTES
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Generate a websocket-rpc server implementation in a class. The class must
/// be partial and implement some methods for the generated code. It also has
/// to annotate all methods callable by the client with <see cref=""WebSocketRpcMethodAttribute""/>.
/// </summary>
/// <param name=""serializationMode"">Serialization mode to use for RPC parameters.</param>
/// <typeparam name=""TClient"">Type of the client connecting to the server.</typeparam>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class WebSocketRpcServerAttribute<TClient>(WebSocketRpcSerializationMode serializationMode) : Attribute
{{
	public WebSocketRpcSerializationMode SerializationMode {{ get; }} = serializationMode;
}}
#endif");
	}
}
