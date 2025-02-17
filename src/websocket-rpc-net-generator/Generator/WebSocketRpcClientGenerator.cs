using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

// No [Generator] attribute as we do not want to automatically generate JavaScript in a .NET build
public sealed class WebSocketRpcClientGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var servers = context.SyntaxProvider
				.CreateSyntaxProvider(WebSocketRpcGenerator.IsServerOrClientCandidate, WebSocketRpcGenerator.ExtractServerModel)
				.Where(model => model is not null);
		var clients = context.SyntaxProvider
				.CreateSyntaxProvider(WebSocketRpcGenerator.IsServerOrClientCandidate, WebSocketRpcGenerator.ExtractClientModel)
				.Where(model => model is not null);
		context.RegisterSourceOutput(servers.Collect().Combine(clients.Collect()),
				(context, source) =>
				{
					foreach (var serverModel in source.Left)
					{
						var clientModel = source.Right.FirstOrDefault(
							client =>
								client!.Value.Class.Namespace.Equals(serverModel!.Value.ClientClassNamespace, StringComparison.Ordinal) &&
								client!.Value.Class.Name.Equals(serverModel!.Value.ClientClassName, StringComparison.Ordinal));
						if (clientModel != null)
						{
							GenerateClientClass(context, serverModel!.Value, clientModel.Value);
						}
					}
				});
	}

	private void GenerateClientClass(SourceProductionContext context, ServerModel serverModel, ClientModel clientModel)
	{
		//
		// Serializer base class
		//
		if (serverModel.Serializer != null || clientModel.Serializer != null)
		{
			var serializerClass = new StringBuilder(@$"
class {clientModel.Class.Name}SerializerBase
{{
	constructor() {{
		if (this.constructor == {clientModel.Class.Name}SerializerBase) {{
			throw new Error('Must not instantiate abstract class ""{clientModel.Class.Name}SerializerBase""');
		}}
	}}
");
			if (serverModel.Serializer != null)
			{
				if (serverModel.Serializer.Value.IsGeneric)
				{
					serializerClass.AppendLine(@$"
	/**
	 * Deserialize data in a generic manner.
	 *
	 * @abstract
	 * @param {{string}} typename Name of the .NET type on the server
	 * @param {{Uint8Array}} data Raw data to deserialize
	 * @returns {{any}} Deserialized object
	 */
	deserialize(typename, data) {{ throw new Error('Must implement abstract method ""deserialize""'); }}");
				}
				else
				{
					foreach (var type in serverModel.Serializer.Value.Types)
					{
						var methodName = $"deserialize{type.EscapedName}";
						serializerClass.AppendLine(@$"
	/**
	 * Deserialize data into the equivalent of the .NET type '{type}' on the server.
	 *
	 * @abstract
	 * @param {{Uint8Array}} data Raw data to deserialize
	 * @returns {{any}} Deserialized object
	 */
	{methodName}(data) {{ throw new Error('Must implement abstract method ""{methodName}""'); }}");
					}
				}
			}

			if (clientModel.Serializer != null)
			{
				if (clientModel.Serializer.Value.IsGeneric)
				{
					serializerClass.AppendLine(@$"
	/**
	 * Serialize data in a generic manner.
	 *
	 * @abstract
	 * @param {{string}} typename Name of the .NET type on the server
	 * @param {{any}} obj Object to serialize
	 * @returns {{Uint8Array}} Serialized data
	 */
	serialize(typename, obj) {{ throw new Error('Must implement abstract method ""serialize""'); }}");
				}
				else
				{
					foreach (var type in clientModel.Serializer.Value.Types)
					{
						var methodName = $"serialize{type.EscapedName}";
						serializerClass.AppendLine(@$"
	/**
	 * Serialize data into the equivalent of the .NET type '{type}' on the server.
	 *
	 * @abstract
	 * @param {{any}} obj Object to serialize
	 * @returns {{Uint8Array}} Serialized data
	 */
	{methodName}(obj) {{ throw new Error('Must implement abstract method ""{methodName}""'); }}");
					}
				}
			}
			serializerClass.Append("}");
			context.AddSource($"{clientModel.Class.Name}SerializerBase.js", serializerClass.ToString());
		}

		//
		// Client base class
		//
		var clientClass = new StringBuilder(@$"
/**
 * @typedef {{import('./{char.ToLower(clientModel.Class.Name[0]) + clientModel.Class.Name.Substring(1)}SerializerBase.js').{clientModel.Class.Name}SerializerBase}} {clientModel.Class.Name}SerializerBase
 */

class {clientModel.Class.Name}Base
{{
	/**
	 * Interval in which to ping the websocket-rpc server. This value should be
	 * lower than the interval the server expects to account for latency.
	 *
	 * Set this to null (which is the default) to not ping the server.
	 *
	 * @type {{number | null}}
	 */
	pingIntervalMs;

	/**
	 * Maximum time to wait for the initial ping message from the server after the
	 * websocket connection has been established. Defaults to 5 seconds.
	 *
	 * @type {{number}}
	 */
	connectionTimeoutMs;

	/**
	 * Underlying websocket of this client.
	 *
	 * @type {{WebSocket | null}}
	 */
	webSocket;

	/** @type {{string}} */
	#url;

	/** @type {{number | undefined}} */
	#pingTaskId;

	/** @type {{null | () => void}} */
	#resolveConnectPromise;

	/** @type {{number | undefined}} */
	#awaitConnectionTaskId;
");
		if (clientModel.Serializer != null || serverModel.Serializer != null)
		{
			clientClass.Append(@$"
	/** @type {{{clientModel.Class.Name}SerializerBase}} */
	#serializer;
	");
		}
		clientClass.Append(@$"
	/**
	 * Create a new client for a websocket-rpc server.
	 *
	 * @param {{string}} url URL of the websocket-rpc server to connect to later.
	 * @param {{{clientModel.Class.Name}SerializerBase | undefined }} serializer Serializer to serialize and deserialize RPC parameters, if needed
	 */
	constructor(url, serializer) {{
		if (this.constructor == {clientModel.Class.Name}Base) {{
			throw new Error('Must not instantiate abstract class ""{clientModel.Class.Name}Base""');
		}}
");
		if (clientModel.Serializer != null || serverModel.Serializer != null)
		{
			clientClass.AppendLine(@$"
		if (serializer == null) {{
			throw new Error('Must provide a serializer since there are RPC parameters');
		}}
		this.#serializer = serializer;");
		}
		clientClass.Append(@$"
		this.pingIntervalMs = null;
		this.connectionTimeoutMs = 5000;
		this.webSocket = null;
		this.#url = url;
		this.#resolveConnectPromise = null;
	}}

	/**
	 * Connect to the websocket-rpc server.
	 *
	 * @throws {{Error}} Already connected or could not connect to the server.
	 * @returns {{Promise}} A promise that resolves once the client has successfully connected. RPC messages may be sent from this point onward.
	 */
	connect() {{
		return new Promise((resolve, reject) => {{
			if (this.webSocket !== null) {{
				reject(new Error('Already connected'));
			}}

			try {{
				this.#resolveConnectPromise = () => {{
					this.#awaitConnectionTaskId = null;
					this.#resolveConnectPromise = null;
					resolve();
				}};

				this.webSocket = new WebSocket(this.#url);
				this.webSocket.binaryType = 'arraybuffer';
				this.webSocket.onopen = () => {{
					this.webSocket.onerror = () => this.onError();
					this.#awaitConnectionTaskId = setTimeout(() => {{
						if (this.#resolveConnectPromise !== null) {{
							reject(new Error('Unable to connect'));
						}}
					}}, this.connectionTimeoutMs);
				}}
				this.webSocket.onmessage = event => this.__onMessage(event);
				this.webSocket.onerror = () => {{
					this.onError(); reject(new Error('Unable to connect'));
				}};
				this.webSocket.onclose = () => {{
					if (this.#awaitConnectionTaskId === null) {{
						this.onDisconnected();
					}}
				}}
			}} catch (e) {{
				clearTimeout(this.#awaitConnectionTaskId);
				clearInterval(this.#pingTaskId);
				this.webSocket = null;
				reject(e);
			}}
		}});
	}}

	/**
	 * Disconnect from the websocket-rpc server.
	 *
	 * @throws {{Error}} Not yet connected or there was an error while disconnecting.
	 * @returns {{Promise}} A promise that resolves once the client has successfully disconnected.
	 */
	disconnect() {{
		return new Promise((resolve, reject) => {{
			if (this.webSocket !== null) {{
				reject(new Error('Not yet connected'));
			}}

			try {{
				this.webSocket.onclose = () => {{ this.onDisconnected(); resolve(); }};
				this.webSocket.close();
			}} catch (e) {{
				reject(e);
			}}
		}});
	}}

	onConnected() {{
		if (this.pingIntervalMs !== null) {{
			clearInterval(this.#pingTaskId);
			this.#pingTaskId = setInterval(() => this.__ping(), this.pingIntervalMs);
		}}
	}}

	onDisconnected() {{
		clearInterval(this.#pingTaskId);
		this.webSocket = null;
	}}

	/**
	 * Does not do anything by default. You may override this to e.g. add re-connection logic.
	 *
	 * @param {{Error | undefined}} error Raised error, undefined if unknown
	 */
	onError(error) {{ }}
");
		//
		// Client methods
		//
		foreach (var method in clientModel.Class.Methods)
		{
			clientClass.Append(@$"
	/**
	 * @abstract");
			foreach (var param in method.Parameters)
			{
				clientClass.Append(@$"
	 * @param {param.Name} Parameter of .NET type '{param.Type}' on the server");
			}
			clientClass.AppendLine(@$"
	 */
	on{method.Name}({WebSocketRpcGenerator.GetParameterList(method.Parameters, types: false)}) {{ throw new Error('Must implement abstract method ""on{method.Name}""'); }}");
		}

		//
		// Server methods
		//
		foreach (var method in serverModel.Class.Methods)
		{
			var name = char.ToLower(method.Name[0]) + method.Name.Substring(1);
			clientClass.Append(@$"
	/**
	 * Call '{method.Name}' (key: {method.Key}) on the server.
	 *");
			foreach (var param in method.Parameters)
			{
				clientClass.Append(@$"
	 * @param {param.Name} Parameter of .NET type '{param.Type}' on the server");
			}
			clientClass.AppendLine(@$"
	 */
	{name}({WebSocketRpcGenerator.GetParameterList(method.Parameters, types: false)}) {{
		var __data = new Uint8Array(4);
		const __view = new DataView(__data.buffer);
		__view.setInt32(0, {method.Key}, true);
		this.webSocket.send(__data);");
			foreach (var param in method.Parameters)
			{
				var serializeCall = serverModel.Serializer!.Value.IsGeneric
					? $"serialize(\"{param.Type}\", {param.Name})"
					: $"serialize{param.Type.EscapedName}({param.Name})";
				clientClass.Append(@$"
		const __{param.Name}__ = this.#serializer.{serializeCall};
		__view.setInt32(0, __{param.Name}__.byteLength, true);
		this.webSocket.send(__data);
		this.webSocket.send(__{param.Name}__);");
			}
			clientClass.AppendLine(@$"
	}}");
		}
		clientClass.Append(@$"
	__ping() {{
		const data = new Uint8Array(4);
		const view = new DataView(data.buffer);
		view.setInt32(0, 0, true);

		this.webSocket.send(data.buffer);
	}}

	/** @param {{MessageEvent}} event WebSocket message sent by the server */
	__onMessage(__event) {{
		try {{
			const __dataView = new DataView(__event.data);
			let __currentOffset = 0;
			while (__currentOffset < __dataView.byteLength) {{
				const __methodKey = __dataView.getInt32(__currentOffset, true);
				__currentOffset += 4;
				switch (__methodKey) {{
					case 0:
						if (this.#resolveConnectPromise !== null) {{
	 						this.onConnected();
							this.#resolveConnectPromise();
							break;
						}}");
		foreach (var method in clientModel.Class.Methods)
		{
			clientClass.Append(@$"
					case {method.Key}:");
			foreach (var param in method.Parameters)
			{
				var deserializeCall = clientModel.Serializer!.Value.IsGeneric
					? $"deserialize(\"{param.Type}\", new Uint8Array(__event.data, __currentOffset, __{param.Name}Length__))"
					: $"deserialize{param.Type.EscapedName}(new Uint8Array(__event.data, __currentOffset, __{param.Name}Length__))";
				clientClass.Append(@$"
						var __{param.Name}Length__ = __dataView.getUint32(__currentOffset, true);
						__currentOffset += 4;
						var {param.Name} = this.#serializer.{deserializeCall};
						__currentOffset += __{param.Name}Length__;");
			}
			clientClass.Append(@$"
						this.on{method.Name}({WebSocketRpcGenerator.GetParameterList(method.Parameters, types: false)});
						break;");
		}
		clientClass.Append(@$"
					default:
						throw new Error(`Invalid method key: ${{__methodKey}}`);
				}}
			}}
		}} catch (e) {{
			this.onError(e);
		}}
	}}
}}");
		context.AddSource($"{clientModel.Class.Name}Base.js", clientClass.ToString());
	}
}
