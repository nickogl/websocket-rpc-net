namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Generate an RPC client in a class.
/// </summary>
/// <remarks>
/// <para>The class must be partial and implement abstract members for the generated code.</para>
/// <para>The class must annotate all methods callable by the server with <see cref="RpcMethodAttribute"/>.</para>
/// </remarks>
/// <param name="parameterSerialization">Serialization method to use for RPC parameters.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RpcClientAttribute(RpcParameterSerialization parameterSerialization) : Attribute
{
	/// <summary>Get the serialization method to use for RPC parameters.</summary>
	public RpcParameterSerialization ParameterSerialization { get; } = parameterSerialization;
}
