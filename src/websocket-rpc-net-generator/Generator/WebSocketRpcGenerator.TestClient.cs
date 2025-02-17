using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	private static TestClientModel? ExtractTestClientModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
	{
		if (context.SemanticModel.GetDeclaredSymbol(context.Node) is not INamedTypeSymbol symbol)
		{
			return null;
		}

		var serverType = ExtractServerUnderTest(symbol);
		if (serverType == null || cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		var serverModel = ExtractServerModel(serverType, cancellationToken, out var serverMetadata);
		if (serverModel == null || serverMetadata == null || cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		var clientModel = ExtractClientModel(serverMetadata.ClientType, cancellationToken, out var clientMetadata);
		if (clientModel == null || clientMetadata == null || cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		var testClientNamespace = GetFullyQualifiedNamespace(symbol.ContainingNamespace);
		var serverSerializerClass = new ClassModel()
		{
			Namespace = testClientNamespace,
			Name = $"{serverModel.Value.Class.Name}Test",
			Methods = serverModel.Value.Class.Methods,
		};
		var clientSerializerClass = new ClassModel()
		{
			Namespace = testClientNamespace,
			Name = $"{clientModel.Value.Class.Name}Test",
			Methods = clientModel.Value.Class.Methods,
		};
		return new TestClientModel()
		{
			ClassNamespace = testClientNamespace,
			ClassName = symbol.Name,
			ServerClass = serverModel.Value.Class,
			ClientClass = clientModel.Value.Class,
			// We need to reverse operations for the test client
			ServerSerializer = CreateSerializerModel(serverSerializerClass, serverMetadata, isClient: true),
			ClientSerializer = CreateSerializerModel(clientSerializerClass, clientMetadata, isClient: false),
		};
	}

	private static INamedTypeSymbol? ExtractServerUnderTest(INamedTypeSymbol symbol)
	{
		foreach (var attribute in symbol.GetAttributes())
		{
			if (attribute.AttributeClass?.Name != "WebSocketRpcTestClientAttribute")
			{
				continue;
			}
			// Abort source generation in case of invalid attribute usage
			if (attribute.AttributeClass.TypeArguments.Length != 1 ||
				attribute.AttributeClass.TypeArguments[0] is not INamedTypeSymbol serverType ||
				!serverType.GetAttributes().Any(attr => attr.AttributeClass?.Name == "WebSocketRpcServerAttribute"))
			{
				return null;
			}

			return serverType;
		}

		return null;
	}

	private static void GenerateTestClientClass(SourceProductionContext context, TestClientModel testClientModel)
	{
		if (testClientModel.ServerSerializer != null)
		{
			GenerateSerializerInterface(context, testClientModel.ServerSerializer.Value);
		}
		if (testClientModel.ClientSerializer != null)
		{
			GenerateSerializerInterface(context, testClientModel.ClientSerializer.Value);
		}

		var testClientClass = new StringBuilder(@$"
#nullable enable

using Nickogl.WebSockets.Rpc;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace {testClientModel.ClassNamespace};

partial class {testClientModel.ClassName} : IAsyncDisposable
{{
	private object _syncRoot = new();
	private ClientWebSocket? _webSocket;
	private string _lastError = ""Not connected yet"";
	private CancellationTokenSource? _cts;
	private Task? _processMessagesTask;
	private Task? _pingTask;
	private __Receiver? _receiver;
	private TaskCompletionSource _connected = new();
	private TaskCompletionSource _disconnected = new();

	/// <summary>Maximum time to wait for a call to be received.</summary>
	private TimeSpan _receiveTimeout = TimeSpan.FromSeconds(5);

	/// <summary>Interval in which to send ping messages to the server.</summary>
	/// <remarks>
	/// Setting this to null disables sending ping messages. This causes the client
	/// to be disconnected if the server requires these messages. Best to set this
	/// to a lower value than the client timeout of the server.
	/// </remarks>
	private TimeSpan? _pingInterval = null;

	/// <summary>Time provider to use when periodically sending pings.</summary>
	private TimeProvider? _timeProvider = null;

	/// <summary>Size of the message buffer in bytes. Defaults to 8 KiB.</summary>
	private int _messageBufferSize = 1024 * 8;

	/// <summary>Maximum size of buffers. Defaults to 16 MiB and only serves as a safeguard against infinite loop bugs.</summary>
	private int _maximumBufferSize = 1024 * 1024 * 16;

	/// <summary>
	/// Configure a websocket before connecting to it.
	/// </summary>
	/// <remarks>You may add authentication headers or cookies here.</remarks>
	/// <param name=""webSocket"">Websocket to configure.</param>
	partial void ConfigureWebSocket(WebSocket webSocket);

	/// <summary>
	/// Wait until the server sent a call matching your provided arguments.
	/// This allows tests to not only verify that clients received certain calls,
	/// but also makes them more resilient because they do not rely on time. It
	/// does not matter if the call already happened or is about to happen.
	/// </summary>
	/// <remarks>
	/// For all methods contained in this object, you can:
	/// <list type=""bullet"">
	/// <item>Require the exact value to be passed</item>
	/// <item>Require the argument to match certain conditions using <see cref=""RpcArg.Is{{T}}""/></item>
	/// <item>Allow the argument to be anything using <see cref=""RpcArg.Any{{T}}""/></item>
	/// </list>
	/// </remarks>
	public __Receiver Received
	{{
		get {{ Debug.Assert(_receiver != null); return _receiver; }}
	}}

	/// <summary>Get and filter a collection of received calls for the individual RPC methods.</summary>
	public __Registries ReceivedCalls
	{{
		get {{ Debug.Assert(_receiver != null); return _receiver.__ReceivedCalls; }}
	}}

	/// <summary>A task that completes once the client has connected to the server.</summary>
	public Task Connected => _connected.Task.WaitAsync(_receiveTimeout);

	/// <summary>A task that completes once the client has disconnected from the server.</summary>
	public Task Disconnected => _disconnected.Task.WaitAsync(_receiveTimeout);

	/// <summary>Get the raw websocket to directly send messages with.</summary>
	public WebSocket WebSocket
	{{
		get {{ Debug.Assert(_webSocket != null); return _webSocket; }}
	}}

	/// <summary>
	/// Connect to a <see cref=""{testClientModel.ServerClass.Namespace}.{testClientModel.ServerClass.Name}"" /> instance located at <paramref name=""uri""/>.
	/// </summary>
	/// <param name=""uri"">URI of the server to connect to.</param>
	public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
	{{
		if (uri.Scheme != Uri.UriSchemeWs && uri.Scheme != Uri.UriSchemeWss)
		{{
			uri = new Uri(uri.ToString().Replace(""http://"", ""ws://"").Replace(""https://"", ""wss://""));
		}}

		lock (_syncRoot)
		{{
			if (_webSocket != null)
			{{
				throw new InvalidOperationException(""Already connected"");
			}}
			_webSocket = new ClientWebSocket();
			ConfigureWebSocket(_webSocket);
		}}
		await _webSocket.ConnectAsync(uri, cancellationToken);

		_disconnected = new TaskCompletionSource();
		_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_receiver = new __Receiver(this);
		_processMessagesTask = ProcessMessages(_webSocket, _receiver, _cts.Token);
		if (_pingInterval != null)
		{{
			_pingTask = PingPeriodically(_webSocket, _pingInterval.Value, _cts.Token);
		}}
		try
		{{
			await Connected;
		}}
		catch (TimeoutException e)
		{{
			throw new TimeoutException($""Client never entered the message processing loop on the server. Last recorded error: {{_lastError}}"", e);
		}}
	}}

	/// <summary>
	/// Disconnect from the server currently connected to.
	/// </summary>
	public async Task DisconnectAsync(CancellationToken cancellationToken = default)
	{{
		var closeTask = Task.CompletedTask;
		lock (_syncRoot)
		{{
			if (_webSocket != null)
			{{
				closeTask = _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
			}}
		}}
		try
		{{
			await closeTask;
		}}
		catch (Exception)
		{{
		}}

		if (_cts != null)
		{{
			_cts.Cancel();
			_cts.Dispose();
			_cts = null;
		}}

		if (_pingTask != null)
		{{
			try {{ await _pingTask; }} catch (Exception) {{ }} finally {{ _pingTask = null; }}
		}}

		if (_processMessagesTask != null)
		{{
			try {{ await _processMessagesTask; }} catch {{ }} finally {{ _processMessagesTask = null; }}
		}}

		_receiver = null;
	}}

	public virtual async ValueTask DisposeAsync()
	{{
		await DisconnectAsync();
	}}

	private async Task PingPeriodically(WebSocket webSocket, TimeSpan interval, CancellationToken cancellationToken)
	{{
		var timeProvider = _timeProvider ?? TimeProvider.System;
		var pingMessage = new byte[4] {{ 0, 0, 0, 0 }};
		while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
		{{
			await webSocket.SendAsync(pingMessage.AsMemory(), WebSocketMessageType.Binary, true, cancellationToken);
			await Task.Delay(interval, timeProvider);
		}}
	}}
");
		if (testClientModel.ServerSerializer != null)
		{
			testClientClass.AppendLine(@$"
	/// <summary>Serializer to serialize parameters sent to server methods.</summary>
	/// <remarks>Initialize this field in the constructor of your test client class.</remarks>
	private {GetFullyQualifiedType(testClientModel.ServerSerializer.Value.InterfaceNamespace, testClientModel.ServerSerializer.Value.InterfaceName)} _serverSerializer;");
		}
		if (testClientModel.ClientSerializer != null)
		{
			testClientClass.AppendLine(@$"
	/// <summary>Serializer to deserialize parameters for calls received from the server.</summary>
	/// <remarks>Initialize this field in the constructor of your test client class.</remarks>
	private {GetFullyQualifiedType(testClientModel.ClientSerializer.Value.InterfaceNamespace, testClientModel.ClientSerializer.Value.InterfaceName)} _clientSerializer;");
		}

		// RPC receive implementation
		testClientClass.Append(@$"
	private async Task ProcessMessages(WebSocket __webSocket, __Receiver __receiver, CancellationToken cancellationToken)
	{{
		try
		{{
			while (__webSocket.State == WebSocketState.Open)
			{{
				var __reader = new MessageReader(_allocator, _messageBufferSize, _maximumMessageSize);
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
								await __webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
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
							// Initial ping message sent as soon as client entered the processing loop
							case 0:
								_connected.TrySetResult();
								break;");
		foreach (var method in testClientModel.ClientClass.Methods)
		{
			var paramsList = GetParameterList(method.Parameters, types: false);
			testClientClass.Append(@$"
							case {method.Key}:
							{{");
			foreach (var param in method.Parameters)
			{
				var deserialize = testClientModel.ClientSerializer!.Value.IsGeneric
					? $"Deserialize<{param.Type.Name}>"
					: $"Deserialize{param.Type.EscapedName}";
				testClientClass.Append(@$"
									__reader.BeginReadParameter();
									{(param.Type.IsDisposable ? "using " : string.Empty)}var {param.Name} = {deserialize}(__reader);
									__reader.EndReadParameter();");
			}
			testClientClass.Append(@$"
								On{method.Name}({paramsList});
								__receiver.__ReceivedCalls.{method.Name}.__AddCall(new({paramsList}));
								break;
							}}
			");
		}
		testClientClass.AppendLine(@$"
							default:
								throw new InvalidDataException($""Method with key '{{__methodKey}}' does not exist"");
						}}
					}}
				}}
				finally
				{{
					__reader.Dispose();
				}}
			}}
		}}
		catch (Exception e)
		{{
			_lastError = $""ProcessMessages() terminated with error: {{e}}"";
		}}
		finally
		{{
			lock (_syncRoot)
			{{
				if (_webSocket != null)
				{{
					_webSocket.Dispose();
					_webSocket = null;
				}}
			}}
			_connected = new TaskCompletionSource();
			_disconnected.TrySetResult();
		}}
	}}");

		//
		// RPC send implementation
		//
		foreach (var method in testClientModel.ServerClass.Methods)
		{
			testClientClass.Append(@$"
	public async ValueTask {method.Name}({GetParameterList(method.Parameters)})
	{{
		Debug.Assert(_webSocket != null, _lastError);
		Debug.Assert(_cts != null, _lastError);

		using var __buffer = new WebSocketRpcBuffer(ArrayPool<byte>.Shared, _messageBufferSize, _maximumBufferSize);
		var __i32Buffer = new byte[4];
		__buffer.WriteMethodKey({method.Key});");
			for (int i = 0; i < method.Parameters.Length; i++)
			{
				// 		testClientClass.Append(@$"
				// __buffer.WriteParameter(_serverSerializer.{GenerateSerializeCall(method.Parameters[i].Type, testClientModel.ServerSerializer, method.Parameters[i].Name)});");
			}
			testClientClass.AppendLine(@$"
		await _webSocket.SendAsync(__buffer.AsMemory(), WebSocketMessageType.Binary, true, _cts.Token);
	}}");
		}
		//
		// Partial RPC receiver methods if event-based flow is needed/desired
		//
		foreach (var method in testClientModel.ClientClass.Methods)
		{
			testClientClass.Append(@$"
	partial void On{method.Name}({GetParameterList(method.Parameters)});
");
		}

		//
		// Interceptor implementation
		//
		testClientClass.AppendLine(@$"
	public sealed class __Registries
	{{
		public interface ICall
		{{
		}}

		public interface IInterceptor<TCall> where TCall : ICall
		{{
			TaskCompletionSource Waiter {{ get; }}

			bool Matches(TCall call);
		}}

		public sealed class Registry<TCall, TInterceptor> : IEnumerable<TCall>
			where TCall : ICall
			where TInterceptor : IInterceptor<TCall>
		{{
			private readonly List<TCall> _calls = [];
			private readonly List<TCall> _unconsumedCalls = [];
			private readonly List<TInterceptor> _interceptors = [];

			public void __AddCall(TCall call)
			{{
				lock (_interceptors)
				{{
					_calls.Add(call);

					var interceptor = _interceptors.FirstOrDefault(interceptor => interceptor.Matches(call));
					if (interceptor != null)
					{{
						interceptor.Waiter.TrySetResult();
						_interceptors.Remove(interceptor);
					}}
					else
					{{
						// Keep for later to potentially consume during interceptor registration
						_unconsumedCalls.Add(call);
					}}
				}}
			}}

			public void __AddInterceptor(TInterceptor interceptor)
			{{
				lock (_interceptors)
				{{
					var call = _unconsumedCalls.FirstOrDefault(call => interceptor.Matches(call));
					if (call != null)
					{{
						interceptor.Waiter.TrySetResult();
						_unconsumedCalls.Remove(call);
					}}
					else
					{{
						// Keep for later to potentially wake up during call registration
						_interceptors.Add(interceptor);
					}}
				}}
			}}

			public IEnumerator<TCall> GetEnumerator()
			{{
				lock (_interceptors)
				{{
					// Copy to ensure thread safety
					return _calls.ToArray().AsEnumerable().GetEnumerator();
				}}
			}}

			IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
		}}");
		foreach (var method in testClientModel.ClientClass.Methods)
		{
			var paramsList = GetParameterList(method.Parameters);
			var argMatcherList = GetArgumentMatcherList(method.Parameters);
			var paramPrefix = method.Parameters.Length > 0 ? ", " : string.Empty;
			testClientClass.AppendLine(@$"
		public sealed record class {method.Name}Call({paramsList}) : ICall
		{{
		}}

		public sealed record class {method.Name}Interceptor(TaskCompletionSource __waiter{paramPrefix}{argMatcherList}) : IInterceptor<{method.Name}Call>
		{{
			public TaskCompletionSource Waiter {{ get; }} = __waiter;

			public bool Matches({method.Name}Call call) => {string.Join(" && ", method.Parameters.Select(param => $"{param.Name}.Matches(call.{param.Name})"))};
		}}

		public Registry<{method.Name}Call, {method.Name}Interceptor> {method.Name} {{ get; }} = new();");
		}
		testClientClass.AppendLine(@$"
	}}");

		testClientClass.AppendLine(@$"
	public sealed class __Receiver({testClientModel.ClassName} client)
	{{
		private {testClientModel.ClassName} _client = client;

		public __Registries __ReceivedCalls {{ get; }} = new();");
		foreach (var method in testClientModel.ClientClass.Methods)
		{
			var paramPrefix = method.Parameters.Length > 0 ? ", " : string.Empty;
			testClientClass.AppendLine(@$"
		public async Task {method.Name}({GetArgumentMatcherList(method.Parameters)})
		{{
			var __waiter = new TaskCompletionSource();
			var __interceptor = new __Registries.{method.Name}Interceptor(__waiter{paramPrefix}{GetParameterList(method.Parameters, types: false)});
			__ReceivedCalls.{method.Name}.__AddInterceptor(__interceptor);
			try
			{{
				// Wait a maximum amount of time to avoid tests running infinitely
				await __waiter.Task.WaitAsync(_client._receiveTimeout, _client._cts?.Token ?? default);
			}}
			catch (TimeoutException e)
			{{
				throw new TimeoutException($""Did not receive a call to '{method.Name}' matching the provided args within {{_client._receiveTimeout.TotalSeconds:n1}} seconds"", e);
			}}
		}}");
		}
		testClientClass.AppendLine(@$"
	}}
}}");

		//
		// Extension methods to conveniently filter received calls registry
		//
		testClientClass.Append(@$"
internal static class {testClientModel.ClassName}Extensions
{{");
		foreach (var method in testClientModel.ClientClass.Methods)
		{
			var paramPrefix = method.Parameters.Length > 0 ? ", " : string.Empty;
			testClientClass.AppendLine(@$"
	public static IEnumerable<{testClientModel.ClassName}.__Registries.{method.Name}Call> Filter(this {testClientModel.ClassName}.__Registries.Registry<{testClientModel.ClassName}.__Registries.{method.Name}Call, {testClientModel.ClassName}.__Registries.{method.Name}Interceptor> __registry{paramPrefix}{GetArgumentMatcherList(method.Parameters)})
	{{
		var __interceptor = new {testClientModel.ClassName}.__Registries.{method.Name}Interceptor(null!{paramPrefix}{GetParameterList(method.Parameters, types: false)});
		return __registry.Where(__interceptor.Matches);
	}}");
		}
		testClientClass.Append("}");
		context.AddSource($"{testClientModel.ClassName}.g.cs", testClientClass.ToString());
	}
}
