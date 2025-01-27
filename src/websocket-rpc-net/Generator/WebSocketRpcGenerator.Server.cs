using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	private static ServerModel? ExtractServerModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
	{
		return context.SemanticModel.GetDeclaredSymbol(context.Node) is INamedTypeSymbol symbol
			? ExtractServerModel(symbol, cancellationToken, out _)
			: null;
	}

	private static ServerModel? ExtractServerModel(INamedTypeSymbol symbol, CancellationToken cancellationToken, out ServerMetadata? metadata)
	{
		metadata = ExtractServerMetadata(symbol);
		if (metadata == null || cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		var classModel = ExtractClassModel(symbol, firstParameterType: metadata.ClientType);
		if (classModel == null || cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		return new ServerModel()
		{
			Class = classModel.Value,
			ClientClassNamespace = GetFullyQualifiedNamespace(metadata.ClientType.ContainingNamespace),
			ClientClassName = metadata.ClientType.Name,
			Serializer = CreateSerializerModel(classModel.Value, metadata, isClient: false),
		};
	}

	private static ServerMetadata? ExtractServerMetadata(INamedTypeSymbol symbol)
	{
		foreach (var attribute in symbol.GetAttributes())
		{
			if (attribute.AttributeClass?.Name != "WebSocketRpcServerAttribute")
			{
				continue;
			}
			// Abort source generation in case of invalid attribute usage
			if (attribute.AttributeClass.TypeArguments.Length != 1 ||
				attribute.AttributeClass.TypeArguments[0] is not INamedTypeSymbol clientSymbolCandidate ||
				attribute.ConstructorArguments.Length != 1 ||
				attribute.ConstructorArguments[0].Value is not int serializationMode ||
				serializationMode < 0 || serializationMode > 1)
			{
				return null;
			}

			foreach (var clientAttribute in clientSymbolCandidate.GetAttributes())
			{
				if (clientAttribute.AttributeClass?.Name == "WebSocketRpcClientAttribute")
				{
					return new() { ClientType = clientSymbolCandidate, UsesGenericSerialization = serializationMode == 0 };
				}
			}
		}

		return null;
	}

	private static void GenerateServerClass(SourceProductionContext context, ServerModel serverModel)
	{
		if (serverModel.Serializer != null)
		{
			GenerateSerializerInterface(context, serverModel.Serializer.Value);
		}

		var serverClass = new StringBuilder(@$"
#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading;

namespace {serverModel.Class.Namespace};

partial class {serverModel.Class.Name}
{{
	/// <summary>Time after which clients are disconnected if they fail to send ping frames.</summary>
	/// <remarks>Setting this to null (default) disables quick detection of disconnects.</remarks>
	private TimeSpan? _clientTimeout = null;

	/// <summary>Time provider to use for enforcing <see cref=""_clientTimeout"" />.</summary>
	private TimeProvider? _timeProvider = null;

	/// <summary>Array pool to use for allocating buffers.</summary>
	private ArrayPool<byte> _allocator = ArrayPool<byte>.Shared;

	/// <summary>Size of the message buffer in bytes. Defaults to 8 KiB.</summary>
	/// <remarks>
	/// Choose one that the vast majority of messages will fit into.
	/// If a message does not fit, the buffer grows exponentially until <see cref=""_maximumMessageSize""/> is reached.
	/// If you are unsure, choose a generous value first and then consult the recorded metrics to refine it.
	/// </remarks>
	private int _messageBufferSize = 1024 * 8;

	/// <summary>Maximum size of messages. Defaults to 64 KiB.</summary>
	/// <remarks>
	/// Choose one that all legit messages will fit into.
	/// If you are unsure, choose a generous value first and then consult the recorded metrics to refine it.
	/// </remarks>
	private int _maximumMessageSize = 1024 * 64;

	/// <summary>Maximum size of parameters. Defaults to 4 KiB.</summary>
	/// <remarks>
	/// Choose one that all parameters of legit messages will fit into.
	/// If you are unsure, choose a generous value first and then consult the recorded metrics to refine it.
	/// </remarks>
	private int _maximumParameterSize = 1024 * 4;
");
		if (serverModel.Serializer != null)
		{
			serverClass.AppendLine(@$"
	/// <summary>Serializer to serialize and deserialize RPC parameters.</summary>
	/// <remarks>Initialize this field in the constructor of your class.</remarks>
	private {GetFullyQualifiedType(serverModel.Serializer.Value.InterfaceNamespace, serverModel.Serializer.Value.InterfaceName)} _serializer;");
		}
		serverClass.Append(@$"
	/// <summary>
	/// Called when the client is about to enter the message processing loop.
	/// </summary>
	private partial ValueTask OnConnectedAsync({GetFullyQualifiedType(serverModel.ClientClassNamespace, serverModel.ClientClassName)} client);

	/// <summary>
	/// Called when the client has disconnected from the server. Its websocket is unusable at this point.
	/// </summary>
	private partial ValueTask OnDisconnectedAsync({GetFullyQualifiedType(serverModel.ClientClassNamespace, serverModel.ClientClassName)} client);

	private async ValueTask SendPingAsync(WebSocket webSocket, CancellationToken cancellationToken)
	{{
		var message = new byte[] {{ 0, 0, 0, 0 }};
		await webSocket.SendAsync(message, WebSocketMessageType.Binary, true, cancellationToken);
	}}

	private sealed record CloseActionState({GetFullyQualifiedType(serverModel.ClientClassNamespace, serverModel.ClientClassName)} Client, CancellationTokenSource Cts)
	{{
	}}

	/// <summary>
	/// Process a client's websocket messages until it disconnects or the provided <paramref name=""cancellationToken""/> is cancelled.
	/// </summary>
	/// <param name=""client"">Client whose websocket messages to process.</param>
	/// <param name=""cancellationToken"">Cancellation token to stop processing messages.</param>
	/// <exception cref=""OperationCanceledException"">The <paramref name=""cancellationToken"" /> was triggered or the client timed out.</exception>
	/// <exception cref=""WebSocketException"">An operation on the client's websocket failed.</exception>
	/// <exception cref=""InvalidDataException"">The client sent an invalid message.</exception>
	/// <returns>A task that represents the lifecycle of the provided client.</returns>
	public async Task ProcessAsync({GetFullyQualifiedType(serverModel.ClientClassNamespace, serverModel.ClientClassName)} client, CancellationToken cancellationToken)
	{{
		if (cancellationToken.IsCancellationRequested)
		{{
			return;
		}}

		// Cancelling the token passed to WebSocket.Send/ReceiveAsync closes the underlying socket,
		// making it impossible to perform the close handshake. So instead of directly passing
		// the cancellation token, we cancel a separate one after performing the close handshake.
		static void CloseWebSocket(object? state)
		{{
			Debug.Assert(state is CloseActionState);
			((CloseActionState)state).Client.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None)
				.ContinueWith(static (_, state) =>
				{{
					Debug.Assert(state is CloseActionState);
					((CloseActionState)state).Cts.Cancel(throwOnFirstException: false);
				}}, state);
		}}

		var __receiveCts = new CancellationTokenSource();
		var __closeActionState = new CloseActionState(client, __receiveCts);
		var __ctReg = cancellationToken.UnsafeRegister(CloseWebSocket, __closeActionState);
		client.Disconnected = __receiveCts.Token;
		ITimer? __clientTimeoutTimer = null;
		if (_clientTimeout != null)
		{{
			__clientTimeoutTimer = (_timeProvider ?? TimeProvider.System)
				.CreateTimer(CloseWebSocket, __closeActionState, _clientTimeout.Value, Timeout.InfiniteTimeSpan);
		}}

		try
		{{
			// Send initial ping message, this allows the client to know when it entered the message processing loop
			await SendPingAsync(client.WebSocket, cancellationToken);

			await OnConnectedAsync(client);
			try
			{{
				int __read = 0;
				int __processed = 0;
				while (client.WebSocket.State == WebSocketState.Open)
				{{
					ValueWebSocketReceiveResult __result = default;
					var __buffer = _allocator.Rent(_messageBufferSize);
					try
					{{
						do
						{{
							{GenerateReadInt32("methodKey", "Incomplete method key", 6)}

							switch (methodKey)
							{{
								case 0:
									if (__clientTimeoutTimer == null || _clientTimeout == null)
									{{
										throw new InvalidDataException(""Unexpected ping frame; the server is not configured to time out clients"");
									}}
									__clientTimeoutTimer.Change(_clientTimeout.Value, Timeout.InfiniteTimeSpan);
									break;
						");
		foreach (var method in serverModel.Class.Methods)
		{
			serverClass.AppendLine(@$"
								//
								// {method.Name}({GenerateParameterList(method.Parameters, types: false)})
								//
								case {method.Key}:");
			foreach (var param in method.Parameters)
			{
				var lengthVariable = $"__{param.Name}Length__";
				var deserialize = GenerateDeserializeCall(param.Type, serverModel.Serializer, innerExpression: "{0}");
				serverClass.Append($@"{Indent(9)}{GenerateReadInt32(lengthVariable, "Incomplete parameter length", 9)}
									if ({lengthVariable} > _maximumParameterSize) throw new InvalidDataException(""Parameter exceeds maximum length: {param.Name}"");
									{GenerateReadExactly(lengthVariable, $"var {param.Name} = _serializer.{deserialize}", "Incomplete parameter data", 9)}");
			}
			serverClass.AppendLine(@$"
									await {method.Name}(client, {GenerateParameterList(method.Parameters, types: false)});
									break;");
		}

		serverClass.AppendLine(@$"
								default:
									throw new InvalidDataException($""Invalid method key: {{methodKey}}"");
							}}
						}} while (!__result.EndOfMessage);
					}}
					finally
					{{
						_allocator.Return(__buffer);
					}}
				}}
			}}
			finally
			{{
				// Cancel outstanding writes
				__receiveCts.Cancel();

				client.WebSocket = null!;
				await OnDisconnectedAsync(client);
			}}
		}}
		finally
		{{
			__clientTimeoutTimer?.Dispose();
			__ctReg.Dispose();
			__receiveCts.Dispose();
		}}
	}}
}}");
		context.AddSource($"{serverModel.Class.Name}.g.cs", serverClass.ToString());

		byte[] newBuffer = new byte[1];
		byte[] buffer = new byte[1];
		buffer.CopyTo(newBuffer.AsSpan());
	}

	private sealed class ServerMetadata : MetadataBase
	{
		public required INamedTypeSymbol ClientType { get; init; }
	}
}
