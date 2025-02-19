using System.Diagnostics;
using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc.Internal;

public abstract partial class RpcServerBase<TClient>
{
	private readonly static byte[] InitialMessagePayload = [0, 0, 0, 0];

	private sealed class CancellationCallbackState
	{
		public required WebSocket WebSocket { get; init; }
		public required CancellationTokenSource ReceiveCts { get; init; }
	}

	/// <summary>
	/// Process a client's RPC messages until it disconnects or the provided <paramref name="cancellationToken"/> is cancelled.
	/// </summary>
	/// <param name="client">Client whose RPC messages to process.</param>
	/// <param name="cancellationToken">Cancellation token to stop processing RPC messages.</param>
	/// <exception cref="OperationCanceledException">The <paramref name="cancellationToken" /> was triggered or the client timed out.</exception>
	/// <exception cref="WebSocketException">An operation on the client's websocket failed.</exception>
	/// <exception cref="InvalidDataException">The client sent an invalid message.</exception>
	/// <returns>A task that represents the lifecycle of the provided client.</returns>
	public virtual async Task ProcessAsync(TClient client, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		static void CancellationCallback(object? state)
		{
			// Cancelling the token passed to WebSocket.Send/ReceiveAsync closes the underlying socket,
			// making it impossible to perform the close handshake. So instead of directly passing
			// the cancellation token, we cancel a separate one after performing the close handshake.
			Debug.Assert(state is CancellationCallbackState);
			((CancellationCallbackState)state).WebSocket
				.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, default)
				.ContinueWith(static (_, state) =>
				{
					Debug.Assert(state is CancellationCallbackState);
					((CancellationCallbackState)state).ReceiveCts.Cancel(throwOnFirstException: false);
				}, state, CancellationToken.None);
		}
		using var receiveCts = new CancellationTokenSource();
		var cancellationCallbackState = new CancellationCallbackState()
		{
			WebSocket = client.WebSocket,
			ReceiveCts = receiveCts
		};
		using var _ = cancellationToken.UnsafeRegister(CancellationCallback, cancellationCallbackState);
		var clientTimeoutTimer = ClientTimeout == null
			? null
			: (TimeProvider ?? TimeProvider.System).CreateTimer(
					CancellationCallback,
					cancellationCallbackState,
					ClientTimeout.Value,
					Timeout.InfiniteTimeSpan);

		try
		{
			await OnConnectedAsync(client).ConfigureAwait(false);
			try
			{
				await client.WebSocket.SendAsync(InitialMessagePayload, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);

				while (client.WebSocket.State == WebSocketState.Open)
				{
					var reader = GetMessageReader();
					try
					{
						ValueWebSocketReceiveResult result = default;
						do
						{
							var buffer = reader.ReceiveBuffer.GetMemory();

							result = await client.WebSocket.ReceiveAsync(buffer, receiveCts.Token).ConfigureAwait(false);
							if (result.MessageType == WebSocketMessageType.Close)
							{
								try
								{
									// Gracefully complete the close handshake
									await client.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, default).ConfigureAwait(false);
								}
								catch
								{
								}
								return;
							}
							if (result.MessageType != WebSocketMessageType.Binary)
							{
								throw new InvalidDataException($"Invalid message type: {result.MessageType}");
							}

							reader.ReceiveBuffer.Advance(result.Count);
						} while (!result.EndOfMessage);

						while (!reader.EndOfMessage)
						{
							var methodKey = reader.ReadMethodKey();
							if (methodKey == 0)
							{
								if (clientTimeoutTimer == null)
								{
									throw new InvalidDataException("Unexpected ping message, the server is not configured to time out clients");
								}
								clientTimeoutTimer.Change(ClientTimeout!.Value, Timeout.InfiniteTimeSpan);
								break;
							}
							else
							{
								await DispatchAsync(client, methodKey, reader).ConfigureAwait(false);
							}
						}
					}
					finally
					{
						ReturnMessageReader(reader);
					}
				}
			}
			finally
			{
				// Cancel outstanding writes
				receiveCts.Cancel();

				await OnDisconnectedAsync(client).ConfigureAwait(false);
			}
		}
		finally
		{
			clientTimeoutTimer?.Dispose();
		}
	}
}
