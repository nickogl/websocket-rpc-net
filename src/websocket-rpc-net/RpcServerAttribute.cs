namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Generate an RPC server in a class.
/// </summary>
/// <remarks>
/// <para>The class must be partial and implement abstract members for the generated code.</para>
/// <para>The class must annotate all methods callable by the client with <see cref="RpcMethodAttribute"/>.</para>
/// </remarks>
/// <param name="parameterSerialization">Serialization method to use for RPC parameters.</param>
/// <typeparam name="TClient">
/// <para>Type of the client connecting to the server.</para>
/// <para>This type must be annotated with <see cref="RpcClientAttribute"/>.</para>
/// </typeparam>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RpcServerAttribute<TClient>(RpcParameterSerialization parameterSerialization) : Attribute
{
	/// <summary>Get the serialization method to use for RPC parameters.</summary>
	public RpcParameterSerialization ParameterSerialization { get; } = parameterSerialization;
}
