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

		var serverSerializerClass = new ClassModel()
		{
			Namespace = serverModel.Value.Class.Namespace,
			Name = $"{serverModel.Value.Class.Name}Test",
			Methods = serverModel.Value.Class.Methods,
		};
		var clientSerializerClass = new ClassModel()
		{
			Namespace = clientModel.Value.Class.Namespace,
			Name = $"{clientModel.Value.Class.Name}Test",
			Methods = clientModel.Value.Class.Methods,
		};
		return new TestClientModel()
		{
			ClassNamespace = GetFullyQualifiedNamespace(symbol.ContainingNamespace),
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
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace {testClientModel.ClassNamespace};

public partial class {testClientModel.ClassName} : IDisposable, IAsyncDisposable
{{
	private ClientWebSocket? _webSocket;
	private CancellationTokenSource? _cts;
	private Task? _processMessagesTask;
	private __Receiver? _receiver;
	private TimeSpan _defaultReceiveTimeout = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Wait until the server sent calls matching your provided arguments.
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
	public __Receiver Receives
	{{
		get {{ Debug.Assert(_receiver != null); return _receiver; }}
	}}

	/// <summary>Get and filter a collection of received calls for the individual RPC methods.</summary>
	public __Registries ReceivedCalls
	{{
		get {{ Debug.Assert(_receiver != null); return _receiver.__ReceivedCalls; }}
	}}

	/// <summary>
	/// Asynchronously connect to a <see cref=""{testClientModel.ServerClass.Namespace}.{testClientModel.ServerClass.Name}"" /> instance located at <paramref name=""uri""/>.
	/// </summary>
	/// <param name=""uri"">URI of the server to connect to.</param>
	public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
	{{
		if (uri.Scheme != Uri.UriSchemeWs && uri.Scheme != Uri.UriSchemeWss)
		{{
			uri = new Uri(uri.ToString().Replace(""http://"", ""ws://"").Replace(""https://"", ""wss://""));
		}}

		_webSocket = new ClientWebSocket();
		await _webSocket.ConnectAsync(uri, cancellationToken);

		_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_receiver = new __Receiver(this);
		_processMessagesTask = ProcessMessages(_webSocket, _receiver, _cts.Token);
	}}

	/// <summary>
	/// Synchronously connect to a <see cref=""{testClientModel.ServerClass.Namespace}.{testClientModel.ServerClass.Name}"" /> instance located at <paramref name=""uri""/>.
	/// </summary>
	/// <param name=""uri"">URI of the server to connect to.</param>
	public void Connect(Uri uri, CancellationToken cancellationToken = default)
	{{
		ConnectAsync(uri, cancellationToken).Wait(cancellationToken);
	}}

	/// <summary>
	/// Asynchronously disconnect from the server currently connected to.
	/// </summary>
	public async Task DisconnectAsync(CancellationToken cancellationToken = default)
	{{
		try
		{{
			_cts?.Cancel();
			if (_webSocket != null)
			{{
				await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
			}}
			if (_processMessagesTask != null)
			{{
				await _processMessagesTask;
			}}
			_webSocket?.Dispose();
		}}
		catch (Exception)
		{{
		}}

		_processMessagesTask = null;
		_receiver = null;
		_cts = null;
		_webSocket = null;
	}}

	/// <summary>
	/// Synchronously disconnect from the server currently connected to.
	/// </summary>
	public void Disconnect(CancellationToken cancellationToken = default)
	{{
		DisconnectAsync(cancellationToken).Wait(cancellationToken);
	}}

	public virtual async ValueTask DisposeAsync()
	{{
		await DisconnectAsync();
	}}

	public virtual void Dispose()
	{{
		var result = DisposeAsync();
		if (!result.IsCompleted)
		{{
			result.AsTask().Wait();
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
	private async Task ProcessMessages(WebSocket webSocket, __Receiver receiver, CancellationToken cancellationToken)
	{{
		while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
		{{
			ValueWebSocketReceiveResult result = default;
			var buffer = new byte[8192];
			var __messageLength = 0;
			do
			{{
				result = await webSocket.ReceiveAsync(buffer.AsMemory(__messageLength), cancellationToken);
				if (result.MessageType == WebSocketMessageType.Close)
				{{
					return;
				}}
				if (result.MessageType != WebSocketMessageType.Binary)
				{{
					throw new InvalidDataException($""Invalid message type: {{result.MessageType}}"");
				}}

				__messageLength += result.Count;
				if (__messageLength == buffer.Length)
				{{
					var newBuffer = new byte[buffer.Length * 2];
					buffer.CopyTo(newBuffer, 0);
					buffer = newBuffer;
				}}
			}} while (!result.EndOfMessage);

			var offset = 0;
			while (offset < __messageLength)
			{{
				int methodKey = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, sizeof(int)));
				offset += sizeof(int);
				switch (methodKey)
				{{");
		foreach (var method in testClientModel.ClientClass.Methods)
		{
			var paramsList = GenerateParameterList(method.Parameters, types: false);
			testClientClass.Append(@$"
					case {method.Key}:");
			foreach (var param in method.Parameters)
			{
				testClientClass.Append(@$"
						var {param.Name}Length = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, sizeof(int)));
						offset += sizeof(int);
						var {param.Name} = _clientSerializer.{GenerateDeserializeCall(param.Type, testClientModel.ClientSerializer, $"buffer.AsSpan(offset, {param.Name}Length)")};
						offset += {param.Name}Length;");
			}
			testClientClass.Append(@$"
						On{method.Name}({paramsList});
						receiver.__ReceivedCalls.{method.Name}.__AddCall(new({paramsList}));
						break;
			");
		}
		testClientClass.AppendLine(@$"
					default:
						throw new InvalidDataException($""Method with key '{{methodKey}}' does not exist"");
				}}
			}}
		}}
	}}");

		//
		// RPC send implementation
		//
		foreach (var method in testClientModel.ServerClass.Methods)
		{
			testClientClass.AppendLine(@$"
	public async ValueTask {method.Name}({GenerateParameterList(method.Parameters)})
	{{
		Debug.Assert(_webSocket != null);
		Debug.Assert(_cts != null);

		var i32Buffer = new byte[4];
		BinaryPrimitives.WriteInt32LittleEndian(i32Buffer, {method.Key});
		await _webSocket.SendAsync(i32Buffer, WebSocketMessageType.Binary, false, _cts.Token);");
			for (int i = 0; i < method.Parameters.Length; i++)
			{
				var endOfMessage = i == (method.Parameters.Length - 1) ? "true" : "false";
				testClientClass.AppendLine(@$"
		var data = _serverSerializer.{GenerateSerializeCall(method.Parameters[i].Type, testClientModel.ServerSerializer, method.Parameters[i].Name)}.ToArray();
		BinaryPrimitives.WriteInt32LittleEndian(i32Buffer, data.Length);
		await _webSocket.SendAsync(i32Buffer, WebSocketMessageType.Binary, false, _cts.Token);
		await _webSocket.SendAsync(data.AsMemory(), WebSocketMessageType.Binary, {endOfMessage}, _cts.Token);");
			}
			testClientClass.AppendLine(@$"
	}}");
		}
		//
		// Partial RPC receiver methods if event-based flow is needed/desired
		//
		foreach (var method in testClientModel.ClientClass.Methods)
		{
			testClientClass.Append(@$"
	partial void On{method.Name}({GenerateParameterList(method.Parameters)});
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
			var paramsList = GenerateParameterList(method.Parameters);
			var argMatcherList = GenerateArgumentMatcherList(method.Parameters);
			testClientClass.AppendLine(@$"
		public sealed record class {method.Name}Call({paramsList}) : ICall
		{{
		}}

		public sealed record class {method.Name}Interceptor(TaskCompletionSource __waiter, {argMatcherList}) : IInterceptor<{method.Name}Call>
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
			testClientClass.AppendLine(@$"
		public async Task {method.Name}({GenerateArgumentMatcherList(method.Parameters)})
		{{
			var waiter = new TaskCompletionSource();
			var interceptor = new __Registries.{method.Name}Interceptor(waiter, {GenerateParameterList(method.Parameters, types: false)});
			__ReceivedCalls.{method.Name}.__AddInterceptor(interceptor);
			try
			{{
				// Wait a maximum amount of time to avoid tests running infinitely
				await waiter.Task.WaitAsync(_client._defaultReceiveTimeout, _client._cts?.Token ?? default);
			}}
			catch (TimeoutException e)
			{{
				throw new TimeoutException($""Did not receive a call to '{method.Name}' matching the provided args within {{_client._defaultReceiveTimeout.TotalSeconds:n1}} seconds"", e);
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
public static class {testClientModel.ClassName}Extensions
{{");
		foreach (var method in testClientModel.ClientClass.Methods)
		{
			testClientClass.AppendLine(@$"
	public static IEnumerable<{testClientModel.ClassName}.__Registries.{method.Name}Call> Filter(this {testClientModel.ClassName}.__Registries.Registry<{testClientModel.ClassName}.__Registries.{method.Name}Call, {testClientModel.ClassName}.__Registries.{method.Name}Interceptor> registry, {GenerateArgumentMatcherList(method.Parameters)})
	{{
		var interceptor = new {testClientModel.ClassName}.__Registries.{method.Name}Interceptor(null!, {GenerateParameterList(method.Parameters, types: false)});
		return registry.Where(interceptor.Matches);
	}}");
		}
		testClientClass.Append("}");
		context.AddSource($"{testClientModel.ClassName}.g.cs", testClientClass.ToString());
	}
}
