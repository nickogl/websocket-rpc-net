using System.Net.WebSockets;

namespace Nickogl.WebSockets.Rpc.Internal;

public abstract partial class RpcServerBase<TClient>
{
	private readonly static byte[] InitialMessagePayload = [0, 0, 0, 0];

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
#if !NET9_0_OR_GREATER
		using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cancellationToken = receiveCts.Token;
		var clientTimeoutTimer = ClientTimeout == null
			? null
			: (TimeProvider ?? TimeProvider.System).CreateTimer(
					state => ((CancellationTokenSource)state!).Cancel(),
					receiveCts,
					ClientTimeout.Value,
					Timeout.InfiniteTimeSpan);
		try
		{
#endif
		client.Disconnected = cancellationToken;

		await OnConnectedAsync(client).ConfigureAwait(false);
		try
		{
			await client.WebSocket.SendAsync(InitialMessagePayload, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);

			while (client.WebSocket.State == WebSocketState.Open)
			{
				var reader = GetMessageReader();
				try
				{
					while (true)
					{
						var buffer = reader.ReceiveBuffer.GetMemory();
						var result = await client.WebSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
						if (result.MessageType == WebSocketMessageType.Close)
						{
							// Gracefully complete the close handshake
							try { await client.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, default).ConfigureAwait(false); } catch { }
							return;
						}
						if (result.MessageType != WebSocketMessageType.Binary)
						{
							throw new InvalidDataException($"Invalid message type: {result.MessageType}");
						}

						reader.ReceiveBuffer.Advance(result.Count);
						if (result.EndOfMessage)
						{
							break;
						}
					}

					while (!reader.EndOfMessage)
					{
						var methodKey = reader.ReadMethodKey();
#if !NET9_0_OR_GREATER
						if (methodKey == 0)
						{
							if (clientTimeoutTimer == null)
							{
								throw new InvalidDataException("Unexpected ping message, the server is not configured to time out clients");
							}
							clientTimeoutTimer.Change(ClientTimeout!.Value, Timeout.InfiniteTimeSpan);
							break;
						}
#endif
						await DispatchAsync(client, methodKey, reader).ConfigureAwait(false);
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
#if !NET9_0_OR_GREATER
				// Cancel outstanding writes
				receiveCts.Cancel();
#endif

			await OnDisconnectedAsync(client).ConfigureAwait(false);
		}
#if !NET9_0_OR_GREATER
		}
		finally
		{
			clientTimeoutTimer?.Dispose();
		}
#endif
	}
}
