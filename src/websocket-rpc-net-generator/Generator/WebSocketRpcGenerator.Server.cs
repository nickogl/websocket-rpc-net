using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	internal static ServerModel? ExtractServerModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
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

using Nickogl.WebSockets.Rpc;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading;

namespace {serverModel.Class.Namespace};

public abstract class {serverModel.Class.Name}Base
{{
	/// <summary>Minimum size of the buffer for received messages in bytes.</summary>
	/// <remarks>
	/// Choose one that the vast majority of messages will fit into.
	/// If you are unsure, choose a generous value first and then adapt to the actual usage in the wild.
	/// If a message does not fit, the buffer grows exponentially until <see cref=""MaximumMessageBufferSize""/> is reached.
	/// </remarks>
	protected abstract int MinimumMessageBufferSize {{ get; }}

	/// <summary>Maximum size of the buffer for received messages in bytes.</summary>
	/// <remarks>
	/// Choose one that all legit messages will fit into.
	/// If you are unsure, choose a generous value first and then adapt to the actual usage in the wild.
	/// If a message exceeds this size, it is dropped and the client forcefully disconnected.
	/// </remarks>
	protected abstract int MaximumMessageBufferSize {{ get; }}

	/// <summary>Array pool to use for allocating buffers for received messages.</summary>
	protected virtual ArrayPool<byte> MessageBufferPool => ArrayPool<byte>.Shared;

	/// <summary>Time after which clients are disconnected if they fail to send ping frames.</summary>
	/// <remarks>Setting this to null (default) disables quick detection of disconnects.</remarks>
	protected virtual TimeSpan? ClientTimeout => null;

	/// <summary>Time provider to use for enforcing <see cref=""ClientTimeout""/>.</summary>
	protected virtual TimeProvider? TimeProvider => null;
");
		if (serverModel.Serializer != null)
		{
			serverClass.AppendLine(@$"
	/// <summary>Serializer to deserialize parameters from RPCs received from clients.</summary>
	protected abstract {GetFullyQualifiedType(serverModel.Serializer.Value.InterfaceNamespace, serverModel.Serializer.Value.InterfaceName)} Serializer {{ get; }}");
		}
		serverClass.Append(@$"
	/// <summary>
	/// Called when the client is about to enter the message processing loop.
	/// </summary>
	protected virtual ValueTask OnConnectedAsync({GetFullyQualifiedType(serverModel.ClientClassNamespace, serverModel.ClientClassName)} client)
	{{
		return ValueTask.CompletedTask;
	}}

	/// <summary>
	/// Called when the client has disconnected from the server. Its websocket is unusable at this point.
	/// </summary>
	protected virtual ValueTask OnDisconnectedAsync({GetFullyQualifiedType(serverModel.ClientClassNamespace, serverModel.ClientClassName)} client)
	{{
		return ValueTask.CompletedTask;
	}}
}}

partial class {serverModel.Class.Name} : {serverModel.Class.Name}Base
{{
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
		if (ClientTimeout != null)
		{{
			__clientTimeoutTimer = (_timeProvider ?? TimeProvider.System)
				.CreateTimer(CloseWebSocket, __closeActionState, ClientTimeout.Value, Timeout.InfiniteTimeSpan);
		}}

		try
		{{
			await OnConnectedAsync(client);

			// Send initial ping message, this allows the client to know when it entered the message processing loop
			await SendPingAsync(client.WebSocket, cancellationToken);

			try
			{{
				while (client.WebSocket.State == WebSocketState.Open)
				{{
					var __reader = new MessageReader(MessageBufferPool, MinimumMessageBufferSize, MaximumMessageBufferSize);
					try
					{{
						ValueWebSocketReceiveResult __result = default;
						do
						{{
							var __receiveBuffer = __reader.GetReceiveBuffer();
							__result = await client.WebSocket.ReceiveAsync(__receiveBuffer, __receiveCts.Token);
							if (__result.MessageType == WebSocketMessageType.Close)
							{{
								try
								{{
									await client.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
								}}
								catch
								{{
								}}
								return;
							}}
							if (__result.MessageType != WebSocketMessageType.Binary)
							{{
								throw new InvalidDataException($""Invalid message type: {{__result.MessageType}}"");
							}}
						}} while (!__result.EndOfMessage);

						while (!__reader.EndOfMessage)
						{{
							var __methodKey = __reader.ReadMethodKey();
							switch (__methodKey)
							{{
								case 0:
									if (__clientTimeoutTimer == null)
									{{
										throw new InvalidDataException(""Unexpected ping frame; the server is not configured to time out clients"");
									}}
									__clientTimeoutTimer.Change(ClientTimeout!.Value, Timeout.InfiniteTimeSpan);
									break;
						");
		foreach (var method in serverModel.Class.Methods)
		{
			serverClass.Append(@$"
								//
								// {method.Name}({GetParameterList(method.Parameters, types: false)})
								//
								case {method.Key}:
								{{");
			foreach (var param in method.Parameters)
			{
				var deserialize = serverModel.Serializer!.Value.IsGeneric
					? $"Serializer.Deserialize<{param.Type.Name}>"
					: $"Serializer.Deserialize{param.Type.EscapedName}";
				serverClass.Append(@$"
									__reader.BeginReadParameter();
									{(param.Type.IsDisposable ? "using " : string.Empty)}var {param.Name} = {deserialize}(__reader);
									__reader.EndReadParameter();");
			}
			var paramPrefix = method.Parameters.Length > 0 ? ", " : string.Empty;
			serverClass.AppendLine(@$"
									await {method.Name}(client{paramPrefix}{GetParameterList(method.Parameters, types: false)});
									break;
								}}");
		}

		serverClass.AppendLine(@$"
								default:
									throw new InvalidDataException($""Invalid method key: {{__methodKey}}"");
							}}
						}}
					}}
					finally
					{{
						__reader.Dispose();
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
	}

	private sealed class ServerMetadata : MetadataBase
	{
		public required INamedTypeSymbol ClientType { get; init; }
	}
}
