using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class RpcServerGenerator
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
		var testClientVisibility = GetAccessibilityString(symbol.DeclaredAccessibility);
		var serverSerializerClass = new ClassModel()
		{
			Namespace = testClientNamespace,
			Name = $"{serverModel.Value.Class.Name}Test",
			Methods = serverModel.Value.Class.Methods,
			Visibility = testClientVisibility,
		};
		var clientSerializerClass = new ClassModel()
		{
			Namespace = testClientNamespace,
			Name = $"{clientModel.Value.Class.Name}Test",
			Methods = clientModel.Value.Class.Methods,
			Visibility = testClientVisibility,
		};
		return new TestClientModel()
		{
			ClassNamespace = testClientNamespace,
			ClassName = symbol.Name,
			ClassVisibility = GetAccessibilityString(symbol.DeclaredAccessibility),
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
			if (!IsRpcTestClientAttribute(attribute))
			{
				continue;
			}
			// Abort source generation in case of invalid attribute usage
			if (attribute.AttributeClass!.TypeArguments.Length != 1 ||
				attribute.AttributeClass.TypeArguments[0] is not INamedTypeSymbol serverType ||
				!serverType.GetAttributes().Any(IsRpcServerAttribute))
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
using Nickogl.WebSockets.Rpc.Serialization;
using Nickogl.WebSockets.Rpc.Testing;
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

{testClientModel.ClassVisibility} abstract class {testClientModel.ClassName}Base
{{
	private readonly RpcMessageWriter _messageWriter = new(new()
	{{
		Pool = ArrayPool<byte>.Shared,
		MinimumSize = 1024 * 8,
		MaximumSize = Array.MaxLength,
	}});

	private readonly RpcMessageReader _messageReader = new(new()
	{{
		Pool = ArrayPool<byte>.Shared,
		MinimumSize = 1024 * 8,
		MaximumSize = Array.MaxLength,
	}});
");
		if (testClientModel.ServerSerializer != null)
		{
			testClientClass.AppendLine(@$"
	/// <summary>Serializer to serialize parameters sent to server methods.</summary>
	protected abstract {GetFullyQualifiedType(testClientModel.ServerSerializer.Value.InterfaceNamespace, testClientModel.ServerSerializer.Value.InterfaceName)} ServerSerializer {{ get; }}");
		}
		if (testClientModel.ClientSerializer != null)
		{
			testClientClass.AppendLine(@$"
	/// <summary>Serializer to deserialize parameters for calls received from the server.</summary>
	protected abstract {GetFullyQualifiedType(testClientModel.ClientSerializer.Value.InterfaceNamespace, testClientModel.ClientSerializer.Value.InterfaceName)} ClientSerializer {{ get; }}");
		}
		testClientClass.Append(@$"
	/// <summary>Maximum time to wait for a call to be received.</summary>
	/// <remarks>
	/// This avoids infinitely running tests in case a call is never sent by the server,
	/// either due to a wrong expectation or a bug. The default of 5 seconds is
	/// conservative, so it is recommended to tweak this value for each server.
	/// </remarks>
	protected virtual TimeSpan ReceiveTimeout => TimeSpan.FromSeconds(5);

	/// <summary>Interval in which to send ping messages to the server.</summary>
	/// <remarks>
	/// Setting this to null disables sending ping messages. This causes the client
	/// to be disconnected if the server requires these messages. Best to set this
	/// to a lower value than <see cref=""RpcServerBase{{T}}.ClientTimeout""/>.
	/// </remarks>
	protected virtual TimeSpan? PingInterval => null;

	/// <summary>Time provider to use for enforcing the <see cref=""PingInterval""/>.</summary>
	/// <remarks>This allows you to control the flow of time in tests.</remarks>
	protected virtual TimeProvider? TimeProvider => null;

	/// <summary>
	/// Configure a websocket before connecting to it.
	/// </summary>
	/// <remarks>You may add authentication headers or cookies here.</remarks>
	/// <param name=""webSocket"">Websocket to configure.</param>
	protected virtual void ConfigureWebSocket(WebSocket webSocket)
	{{
	}}

	/// <summary>See: <see cref=""RpcClientBase.GetMessageWriter""/></summary>
	protected virtual IRpcMessageWriter GetMessageWriter()
	{{
		return _messageWriter;
	}}

	/// <summary>See: <see cref=""RpcClientBase.ReturnMessageWriter""/></summary>
	protected virtual void ReturnMessageWriter(IRpcMessageWriter messageWriter)
	{{
		messageWriter.Reset();
	}}

	/// <summary>See: <see cref=""RpcServerBase{{T}}.GetMessageReader""/></summary>
	protected virtual IRpcMessageReader GetMessageReader()
	{{
		return _messageReader;
	}}

	/// <summary>See: <see cref=""RpcServerBase{{T}}.ReturnMessageReader""/></summary>
	protected virtual void ReturnMessageReader(IRpcMessageReader messageReader)
	{{
		messageReader.Reset();
	}}
}}

{testClientModel.ClassVisibility} partial class {testClientModel.ClassName} : {testClientModel.ClassName}Base, IAsyncDisposable
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

	/// <summary>
	/// Wait until the server sent a call matching your provided arguments.
	/// This allows tests to not only verify that clients received certain calls,
	/// but also makes them more resilient because they do not rely on time. It
	/// does not matter if the call already happened or is about to happen.
	/// </summary>
	/// <remarks>
	/// For all parameters for methods contained in this object, you can:
	/// <list type=""bullet"">
	/// <item>Require the exact value to be passed</item>
	/// <item>Require the argument to match certain conditions using <see cref=""RpcParameter.Is{{T}}""/></item>
	/// <item>Allow the argument to be anything using <see cref=""RpcParameter.Any{{T}}""/></item>
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
	public Task Connected => _connected.Task.WaitAsync(ReceiveTimeout);

	/// <summary>A task that completes once the client has disconnected from the server.</summary>
	public Task Disconnected => _disconnected.Task.WaitAsync(ReceiveTimeout);

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
		if (PingInterval != null)
		{{
			_pingTask = PingPeriodically(_webSocket, PingInterval.Value, _cts.Token);
		}}
		try
		{{
			await Connected.ConfigureAwait(false);
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
			await closeTask.ConfigureAwait(false);
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
			try {{ await _pingTask.ConfigureAwait(false); }} catch (Exception) {{ }} finally {{ _pingTask = null; }}
		}}

		if (_processMessagesTask != null)
		{{
			try {{ await _processMessagesTask.ConfigureAwait(false); }} catch {{ }} finally {{ _processMessagesTask = null; }}
		}}

		_receiver = null;
	}}

	public virtual async ValueTask DisposeAsync()
	{{
		await DisconnectAsync().ConfigureAwait(false);
	}}

	private async Task PingPeriodically(WebSocket webSocket, TimeSpan interval, CancellationToken cancellationToken)
	{{
		var timeProvider = TimeProvider ?? TimeProvider.System;
		var pingMessage = new byte[4] {{ 0, 0, 0, 0 }};
		while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
		{{
			await webSocket.SendAsync(pingMessage.AsMemory(), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
			await Task.Delay(interval, timeProvider).ConfigureAwait(false);
		}}
	}}
");
		// RPC receive implementation
		testClientClass.Append(@$"
	private async Task ProcessMessages(WebSocket __webSocket, __Receiver __receiver, CancellationToken cancellationToken)
	{{
		try
		{{
			while (__webSocket.State == WebSocketState.Open)
			{{
				var __reader = GetMessageReader();
				try
				{{
					ValueWebSocketReceiveResult __result = default;
					do
					{{
						var __buffer = __reader.ReceiveBuffer.GetMemory();
						__result = await __webSocket.ReceiveAsync(__buffer, cancellationToken).ConfigureAwait(false);
						if (__result.MessageType == WebSocketMessageType.Close)
						{{
							try
							{{
								await __webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, default).ConfigureAwait(false);
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
						__reader.ReceiveBuffer.Advance(__result.Count);
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
					? $"ClientSerializer.Deserialize<{param.Type.Name}>"
					: $"ClientSerializer.Deserialize{param.Type.EscapedName}";
				testClientClass.Append(@$"
									__reader.BeginReadParameter();
									{(param.Type.IsDisposable ? "using " : string.Empty)}var {param.Name} = {deserialize}(__reader.ParameterReader);
									__reader.EndReadParameter();");
			}
			testClientClass.Append(@$"
								On{method.Name}({paramsList});
								var call = new __Registries.__{method.Name}Call({paramsList});
								__receiver.__ReceivedCalls.__All.Add(call);
								__receiver.__ReceivedCalls.{method.Name}.AddCall(call);
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

		var __messageWriter = GetMessageWriter();
		try
		{{
			__messageWriter.WriteMethodKey({method.Key});");
			foreach (var param in method.Parameters)
			{
				var serialize = testClientModel.ServerSerializer!.Value.IsGeneric
					? $"ServerSerializer.Serialize<{param.Type.Name}>"
					: $"ServerSerializer.Serialize{param.Type.EscapedName}";
				testClientClass.Append(@$"
			__messageWriter.BeginWriteParameter();
			{serialize}(__messageWriter.ParameterWriter, {param.Name});
			__messageWriter.EndWriteParameter();");
			}
			testClientClass.AppendLine(@$"
			await _webSocket.SendAsync(__messageWriter.WrittenMemory, WebSocketMessageType.Binary, true, _cts.Token);
		}}
		finally
		{{
			ReturnMessageWriter(__messageWriter);
		}}
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

		public sealed class __Registry<TCall, TInterceptor> : IEnumerable<TCall>
			where TCall : ICall
			where TInterceptor : IInterceptor<TCall>
		{{
			private readonly List<TCall> _calls = [];
			private readonly List<TCall> _unconsumedCalls = [];
			private readonly List<TInterceptor> _interceptors = [];

			public void AddCall(TCall call)
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

			public void AddInterceptor(TInterceptor interceptor)
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
		}}

		public List<ICall> __All {{ get; }} = new();");
		foreach (var method in testClientModel.ClientClass.Methods)
		{
			var paramsList = GetParameterList(method.Parameters);
			var argMatcherList = GetParameterMatcherList(method.Parameters);
			var paramPrefix = method.Parameters.Length > 0 ? ", " : string.Empty;
			testClientClass.Append(@$"
		public sealed record class __{method.Name}Call({paramsList}) : ICall
		{{
			public override string ToString()
			{{
				var result = new StringBuilder(""{method.Name}("");");
			for (int i = 0; i < method.Parameters.Length; i++)
			{
				if (method.Parameters[i].Type.Name == "System.String")
				{
					testClientClass.Append(@$"
				result.Append('""');
				result.Append({method.Parameters[i].Name});
				result.Append('""');");
				}
				else
				{
					testClientClass.Append(@$"
				result.Append({method.Parameters[i].Name}.ToString());");
				}
				if (i != method.Parameters.Length - 1)
				{
					testClientClass.Append("result.Append(\", \")");
				}
			}
			testClientClass.Append(@$"
				return result.Append(')').ToString();
			}}
		}}

		public sealed record class __{method.Name}Interceptor(TaskCompletionSource __waiter{paramPrefix}{argMatcherList}) : IInterceptor<__{method.Name}Call>
		{{
			public TaskCompletionSource Waiter {{ get; }} = __waiter;

			public bool Matches(__{method.Name}Call call) => {string.Join(" && ", method.Parameters.Select(param => $"{param.Name}.Matches(call.{param.Name})"))};
		}}

		public __Registry<__{method.Name}Call, __{method.Name}Interceptor> {method.Name} {{ get; }} = new();");
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
		public async Task {method.Name}({GetParameterMatcherList(method.Parameters)})
		{{
			var __waiter = new TaskCompletionSource();
			var __interceptor = new __Registries.__{method.Name}Interceptor(__waiter{paramPrefix}{GetParameterList(method.Parameters, types: false)});
			__ReceivedCalls.{method.Name}.AddInterceptor(__interceptor);
			try
			{{
				// Wait a maximum amount of time to avoid tests running infinitely
				await __waiter.Task.WaitAsync(_client.ReceiveTimeout, _client._cts?.Token ?? default);
			}}
			catch (TimeoutException e)
			{{
				throw new TimeoutException($""Did not receive a call to '{method.Name}' matching the provided args within {{_client.ReceiveTimeout.TotalSeconds:n1}} seconds.\nReceived calls in order:\n- {{string.Join(""\n- "", __ReceivedCalls.__All.Select(call => call.ToString()))}}"", e);
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
	public static IEnumerable<{testClientModel.ClassName}.__Registries.__{method.Name}Call> Filter(this {testClientModel.ClassName}.__Registries.__Registry<{testClientModel.ClassName}.__Registries.__{method.Name}Call, {testClientModel.ClassName}.__Registries.__{method.Name}Interceptor> __registry{paramPrefix}{GetParameterMatcherList(method.Parameters)})
	{{
		var __interceptor = new {testClientModel.ClassName}.__Registries.__{method.Name}Interceptor(null!{paramPrefix}{GetParameterList(method.Parameters, types: false)});
		return __registry.Where(__interceptor.Matches);
	}}");
		}
		testClientClass.Append("}");
		context.AddSource($"{testClientModel.ClassName}.g.cs", testClientClass.ToString());
	}
}
