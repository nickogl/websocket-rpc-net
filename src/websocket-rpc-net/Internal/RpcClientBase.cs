using Nickogl.WebSockets.Rpc.Serialization;
using System.Buffers;
using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc.Internal;

/// <summary>
/// Base class for every generated RPC client.
/// </summary>
public abstract class RpcClientBase
{
	/// <summary>Get the underlying websocket for this client.</summary>
	public abstract WebSocket WebSocket { get; }

	/// <summary>Get or set the cancellation token that triggers when the client is about to disconnect.</summary>
	public CancellationToken Disconnected { get; set; }

	/// <summary>
	/// Send an RPC message built with the provided <paramref name="messageWriter"/>.
	/// </summary>
	/// <remarks>
	/// This can be called with the same <paramref name="messageWriter"/> for multiple
	/// different clients to essentially broadcast a message and avoid paying for
	/// the message serialization cost multiple times.
	/// </remarks>
	/// <param name="messageWriter">Message writer whose contents to flush.</param>
	public ValueTask FlushAsync(IRpcMessageWriter messageWriter)
	{
		return WebSocket.SendAsync(
			buffer: messageWriter.WrittenMemory,
			messageType: WebSocketMessageType.Binary,
			endOfMessage: true,
			cancellationToken: Disconnected);
	}

	/// <summary>
	/// Get an RPC message writer for writing a single message.
	/// </summary>
	/// <remarks>
	/// <para>
	/// By default, the writers's underlying buffer uses the shared array pool,
	/// allocates a minimum of 1 KiB and grows exponentially until 64 KiB, after
	/// which it throws an exception. If you need to tweak these options, override
	/// this method.
	/// </para>
	/// <para>
	/// By default, this allocates a new instance of <see cref="RpcMessageWriter"/>
	/// every time you call a single method on the client. Depending on your load
	/// profile you may want to re-use or pool these instances, but always make sure
	/// to benchmark your RPC server to see if this has any benefits.
	/// </para>
	/// <para>
	/// Alternatively, you can create instances of <see cref="IRpcMessageWriter"/>
	/// yourself and use the overloads that take one as their first parameter, then
	/// call <see cref="FlushAsync"/> to send the remote procedure call(s).
	/// </para>
	/// </remarks>
	protected virtual IRpcMessageWriter GetMessageWriter()
	{
		return new RpcMessageWriter(new()
		{
			Pool = ArrayPool<byte>.Shared,
			MinimumSize = 1024,
			MaximumSize = 1024 * 64,
		});
	}

	/// <summary>
	/// Return the RPC message writer acquired by <see cref="GetMessageWriter"/>.
	/// </summary>
	/// <param name="messageWriter">RPC message writer to return.</param>
	protected virtual void ReturnMessageWriter(IRpcMessageWriter messageWriter)
	{
		messageWriter.Dispose();
	}
}
