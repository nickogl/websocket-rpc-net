namespace Nickogl.WebSockets.Rpc.Testing;

/// <summary>
/// Generate an RPC client for use in integration tests.
/// </summary>
/// <remarks>
/// <para>The class must be partial and implement abstract members for the generated code.</para>
/// <para>
/// The generated part contains methods for calling methods on the server.
/// It also contains members for verifying (or awaiting) calls received from the server.
/// This allows creating reproducible tests that do not rely on certain timings to work.
/// </para>
/// </remarks>
/// <typeparam name="TServer">The websocket-rpc server type under test.</typeparam>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RpcTestClientAttribute<TServer> : Attribute
{
}
