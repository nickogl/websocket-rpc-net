using System.Net.WebSockets;
using System.Threading.Channels;

namespace Nickogl.WebSockets.Rpc.UnitTest;

/// <summary>
/// A websocket for testing that records the sent data and controls the received data.
/// </summary>
/// <remarks>
/// <para>
/// Check the data sent by <see cref="SendAsync"/> with <see cref="SentMessageSegments"/>.
/// </para>
/// <para>
/// Send data to be consumed by <see cref="ReceiveAsync"/> with <see cref="ReceiveMessageSegmentAsync"/>.
/// </para>
/// </remarks>
internal sealed class FakeWebSocket : WebSocket
{
	public sealed class MessageSegment
	{
		public readonly static MessageSegment Close = new() { Data = [], Type = WebSocketMessageType.Close, EndOfMessage = true };

		public required ArraySegment<byte> Data { get; init; }
		public required WebSocketMessageType Type { get; init; }
		public required bool EndOfMessage { get; init; }
	}

	private readonly object _syncObj = new();
	private readonly List<MessageSegment> _sentMessageSegments = [];
	private readonly Channel<MessageSegment> _receivedMessageChannel = Channel.CreateUnbounded<MessageSegment>(new() { SingleReader = true, SingleWriter = true });
	private Dictionary<MessageSegment, TaskCompletionSource> _receiveMessagePromises = [];
	private WebSocketState _state = WebSocketState.Open;
	private WebSocketCloseStatus? _closeStatus;
	private string? _closeStatusDescription;

	/// <summary>Timeout to receive message data. Prevents infinitely running tests.</summary>
	public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(3);

	/// <summary>Get all message segments sent with <see cref="SendAsync"/>.</summary>
	public IReadOnlyCollection<MessageSegment> SentMessageSegments
	{
		get
		{
			lock (_syncObj)
			{
				return [.. _sentMessageSegments];
			}
		}
	}

	public override WebSocketCloseStatus? CloseStatus => _closeStatus;
	public override string? CloseStatusDescription => _closeStatusDescription;
	public override WebSocketState State => _state;
	public override string? SubProtocol => null;

	/// <summary>
	/// Queue the provided message segment and wait for it to be received in <see cref="ReceiveAsync"/>.
	/// </summary>
	/// <remarks>
	/// This operation blocks until the provided segment is consumed by <see cref="ReceiveAsync"/>.
	/// </remarks>
	/// <param name="data">Data to receive.</param>
	/// <param name="type">Type of the message segment.</param>
	/// <param name="endOfMessage">Whether this segment denotes the end of the message.</param>
	/// <returns>A task that completes once the segment was consumed by <see cref="ReceiveAsync"/>.</returns>
	public async Task ReceiveMessageSegmentAsync(byte[] data, WebSocketMessageType type, bool endOfMessage, CancellationToken cancellationToken = default)
	{
		var segment = new MessageSegment() { Data = data, Type = type, EndOfMessage = endOfMessage };
		var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (_syncObj)
		{
			if (_state != WebSocketState.Open)
			{
				throw new InvalidOperationException($"Could not queue message statement because the websocket's state is '{_state}'");
			}

			_receiveMessagePromises.Add(segment, tcs);
		}

		await _receivedMessageChannel.Writer.WriteAsync(segment, cancellationToken);
		try
		{
			await tcs.Task.WaitAsync(Timeout, cancellationToken);
		}
		catch (TimeoutException e)
		{
			throw new TimeoutException($"FakeWebSocket.ReceiveMessageSegmentAsync(): Safety timeout of {Timeout.TotalSeconds:n2} seconds was triggered", e);
		}
	}

	public override void Abort()
	{
		lock (_syncObj)
		{
			_state = WebSocketState.Aborted;

			_receivedMessageChannel.Writer.TryComplete();
		}
	}

	public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken = default)
	{
		lock (_syncObj)
		{
			CloseInternal(closeStatus, statusDescription);
			_state = WebSocketState.CloseSent;

			// Send an acknowledgement for ReceiveAsync() to consume, which will set
			// the state to WebSocketState.Closed
			_receivedMessageChannel.Writer.TryWrite(MessageSegment.Close);
		}

		return Task.CompletedTask;
	}

	public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken = default)
	{
		// We initiate the close handshake in a fire-and-forget manner, thus we will
		// not send an acknowledgement for ReceiveAsync() to consume
		lock (_syncObj)
		{
			CloseInternal(closeStatus, statusDescription);
			_state = WebSocketState.Closed;
		}

		return Task.CompletedTask;
	}

	public override void Dispose()
	{
		_receivedMessageChannel.Writer.TryComplete();
	}

	public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = default)
	{
		lock (_syncObj)
		{
			// Allow reading the rest of the received segments if a close message was received
			if (_state != WebSocketState.Open && _state != WebSocketState.CloseSent)
			{
				throw new InvalidOperationException($"Could not receive data because the websocket's state is '{_state}'");
			}
		}

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(Timeout);
		try
		{
			var segment = await _receivedMessageChannel.Reader.ReadAsync(cts.Token);
			if (segment.Type == WebSocketMessageType.Close)
			{
				lock (_syncObj) { _state = WebSocketState.Closed; }
				return new(0, WebSocketMessageType.Close, true, _closeStatus, _closeStatusDescription);
			}
			else
			{
				if (_receiveMessagePromises.TryGetValue(segment, out var promise))
				{
					promise.TrySetResult();
				}
				segment.Data.CopyTo(buffer);
				return new(segment.Data.Count, segment.Type, segment.EndOfMessage);
			}
		}
		catch (ChannelClosedException e)
		{
			throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely, e);
		}
		catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException($"FakeWebSocket.ReceiveAsync(): Safety timeout of {Timeout.TotalSeconds:n2} seconds was triggered", e);
		}
		catch (OperationCanceledException)
		{
			// Simulate .NET's ManagedWebSocket behavior
			Abort();
			throw;
		}
	}

	public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken = default)
	{
		lock (_syncObj)
		{
			if (_state != WebSocketState.Open)
			{
				throw new InvalidOperationException($"Could not send data because the websocket's state is '{_state}'");
			}

			if (cancellationToken.IsCancellationRequested)
			{
				// Simulate .NET's ManagedWebSocket behavior
				Abort();
			}
			else
			{
				_sentMessageSegments.Add(new MessageSegment() { Data = buffer, Type = messageType, EndOfMessage = endOfMessage });
			}
		}

		return Task.CompletedTask;
	}

	private void CloseInternal(WebSocketCloseStatus closeStatus, string? statusDescription)
	{
		lock (_syncObj)
		{
			if (_state == WebSocketState.Closed || _state == WebSocketState.CloseSent)
			{
				return;
			}
			if (_state != WebSocketState.Open)
			{
				throw new InvalidOperationException($"Could not close the websocket because its state is '{_state}'");
			}

			_closeStatus = closeStatus;
			_closeStatusDescription = statusDescription;

			_sentMessageSegments.Add(MessageSegment.Close);
		}
	}
}
