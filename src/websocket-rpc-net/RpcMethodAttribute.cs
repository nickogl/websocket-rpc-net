namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Mark a method to be called by the RPC server or client.
/// </summary>
/// <remarks>
/// <para>In a server context, it has to additionally take the client as its first parameter.</para>
/// <para>In all contexts it must return a <see cref="ValueTask"/>.</para>
/// </remarks>
/// <param name="key">
/// <para>Consistent, unique key of this method.</para>
/// <para>It must be 1 or greater. 0 is reserved for internal use.</para>
/// <para>Changing this value on a method breaks existing clients.</para>
/// </param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RpcMethodAttribute(int key) : Attribute
{
	/// <summary>Get the consistent, unique key of this method.</summary>
	public int Key { get; } = key;
}
