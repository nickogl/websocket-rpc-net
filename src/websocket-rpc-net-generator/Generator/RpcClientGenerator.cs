using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

// No [Generator] attribute as we do not want to automatically generate JavaScript in a .NET build
public sealed class RpcClientGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		context.RegisterPostInitializationOutput(GenerateUtilities);

		var servers = context.SyntaxProvider
				.CreateSyntaxProvider(RpcServerGenerator.IsServerOrClientCandidate, RpcServerGenerator.ExtractServerModel)
				.Where(model => model is not null);
		var clients = context.SyntaxProvider
				.CreateSyntaxProvider(RpcServerGenerator.IsServerOrClientCandidate, RpcServerGenerator.ExtractClientModel)
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

	private void GenerateUtilities(IncrementalGeneratorPostInitializationContext context)
	{
		context.AddSource("rpcMessageWriter.js", @$"
export class RpcMessageWriter
{{
	_buffer;
	_dataView;
	_typedArray;
	_written;

	/**
	 * Create a new RPC message writer.
	 *
	 * @param {{number | undefined}} initialBufferSize Initial size of the message buffer. Grows expontentially as needed.
	 */
	constructor(initialBufferSize) {{
		this._buffer = new ArrayBuffer(initialBufferSize || 4096);
		this._dataView = new DataView(this._buffer);
		this._typedArray = new Uint8Array(this._buffer);
		this._written = 0;
	}}

	/**
	 * Get the written data thus far.
	 *
	 * @returns {{Uint8Array}}
	 */
	get data() {{
		return new Uint8Array(this._buffer, 0, this._written);
	}}

	/**
	 * Reset this instance for subsequent use. Keeps the allocated buffer.
	 */
	reset() {{
		this._written = 0;
	}}

	/**
	 * Write an RPC method key.
	 *
	 * @param {{number}} key Key of the method to call on the server.
	 */
	writeMethodKey(key) {{
		this._ensureAtLeast(4);
		this._dataView.setInt32(this._written, key, true);
		this._written += 4;
	}}

	/**
	 * Write RPC parameter data.
	 *
	 * @param {{Uint8Array}} data Raw data of the parameter.
	 */
	writeParameter(data) {{
		this._ensureAtLeast(data.byteLength + 4);
		this._dataView.setInt32(this._written, data.byteLength, true);
		this._written += 4;
		this._typedArray.set(data, this._written);
		this._written += data.byteLength;
	}}

	_ensureAtLeast(size) {{
		const requiredSize = this._written + size;
		if (requiredSize > this._buffer.byteLength) {{
			let newSize = this._buffer.byteLength;
			while (newSize < requiredSize) {{
				newSize *= 2;
			}}

			const newBuffer = new ArrayBuffer(newSize);
			this._dataView = new DataView(newBuffer);
			this._typedArray = new Uint8Array(newBuffer);
			this._typedArray.set(this.data, 0);
			this._buffer = newBuffer;
		}}
	}}
}}");
	}

	private void GenerateClientClass(SourceProductionContext context, ServerModel serverModel, ClientModel clientModel)
	{
		//
		// Serializer base class
		//
		if (serverModel.Serializer != null || clientModel.Serializer != null)
		{
			var serializerClass = new StringBuilder(@$"
export class {clientModel.Class.Name}SerializerBase
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
	 * Deserialize data into the equivalent of the .NET type '{type.Name}' on the server.
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
	 * Serialize data into the equivalent of the .NET type '{type.Name}' on the server.
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

import {{ RpcMessageWriter }} from './rpcMessageWriter.js';

export class {clientModel.Class.Name}Base
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
	_url;

	/** @type {{number | undefined}} */
	_pingTaskId;

	/** @type {{null | () => void}} */
	_resolveConnectPromise;

	/** @type {{number | undefined}} */
	_awaitConnectionTaskId;

	/** @type {{RpcMessageWriter}} */
	_messageWriter;
");
		if (clientModel.Serializer != null || serverModel.Serializer != null)
		{
			clientClass.Append(@$"
	/** @type {{{clientModel.Class.Name}SerializerBase}} */
	_serializer;
	");
		}
		clientClass.Append(@$"
	/**
	 * Create a new client for a websocket-rpc server.
	 *
	 * @param {{string}} url URL of the websocket-rpc server to connect to later.
	 * @param {{{clientModel.Class.Name}SerializerBase | undefined }} serializer Serializer to serialize and deserialize RPC parameters, if needed.
	 * @param {{RpcMessageWriter}} messageWriter Message writer to use. If not provided, creates a new one for every message.
	 */
	constructor(url, serializer, messageWriter) {{
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
		this._serializer = serializer;");
		}
		clientClass.Append(@$"
		this.pingIntervalMs = null;
		this.connectionTimeoutMs = 5000;
		this.webSocket = null;
		this._url = url;
		this._resolveConnectPromise = null;
		this._messageWriter = messageWriter;
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
				this._resolveConnectPromise = () => {{
					this._awaitConnectionTaskId = null;
					this._resolveConnectPromise = null;
					resolve();
				}};

				this.webSocket = new WebSocket(this._url);
				this.webSocket.binaryType = 'arraybuffer';
				this.webSocket.onopen = () => {{
					this.webSocket.onerror = () => this.onError();
					this._awaitConnectionTaskId = setTimeout(() => {{
						if (this._resolveConnectPromise !== null) {{
							reject(new Error('Unable to connect'));
						}}
					}}, this.connectionTimeoutMs);
				}}
				this.webSocket.onmessage = event => this.__onMessage(event);
				this.webSocket.onerror = () => {{
					this.onError(); reject(new Error('Unable to connect'));
				}};
				this.webSocket.onclose = () => {{
					if (this._awaitConnectionTaskId === null) {{
						this.onDisconnected();
					}}
				}}
			}} catch (e) {{
				clearTimeout(this._awaitConnectionTaskId);
				clearInterval(this._pingTaskId);
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
			clearInterval(this._pingTaskId);
			this._pingTaskId = setInterval(() => this.__ping(), this.pingIntervalMs);
		}}
	}}

	onDisconnected() {{
		clearInterval(this._pingTaskId);
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
	 * @param {param.Name} Parameter of .NET type '{param.Type.Name}' on the server");
			}
			clientClass.AppendLine(@$"
	 */
	on{method.Name}({RpcServerGenerator.GetParameterList(method.Parameters, types: false)}) {{ throw new Error('Must implement abstract method ""on{method.Name}""'); }}");
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
	 * @param {param.Name} Parameter of .NET type '{param.Type.Name}' on the server");
			}
			clientClass.Append(@$"
	 */
	{name}({RpcServerGenerator.GetParameterList(method.Parameters, types: false)}) {{
		var __writer = this._messageWriter || new RpcMessageWriter();
		__writer.writeMethodKey({method.Key});");
			foreach (var param in method.Parameters)
			{
				var serializeCall = serverModel.Serializer!.Value.IsGeneric
					? $"serialize(\"{param.Type.Name}\", {param.Name})"
					: $"serialize{param.Type.EscapedName}({param.Name})";
				clientClass.Append(@$"
		__writer.writeParameter(this._serializer.{serializeCall});");
			}
			clientClass.AppendLine(@$"
		this.webSocket.send(__writer.data);
		__writer.reset();
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
						if (this._resolveConnectPromise !== null) {{
	 						this.onConnected();
							this._resolveConnectPromise();
							break;
						}}");
		foreach (var method in clientModel.Class.Methods)
		{
			clientClass.Append(@$"
					case {method.Key}:");
			foreach (var param in method.Parameters)
			{
				var deserializeCall = clientModel.Serializer!.Value.IsGeneric
					? $"deserialize(\"{param.Type.Name}\", new Uint8Array(__event.data, __currentOffset, __{param.Name}Length__))"
					: $"deserialize{param.Type.EscapedName}(new Uint8Array(__event.data, __currentOffset, __{param.Name}Length__))";
				clientClass.Append(@$"
						var __{param.Name}Length__ = __dataView.getUint32(__currentOffset, true);
						__currentOffset += 4;
						var {param.Name} = this._serializer.{deserializeCall};
						__currentOffset += __{param.Name}Length__;");
			}
			clientClass.Append(@$"
						this.on{method.Name}({RpcServerGenerator.GetParameterList(method.Parameters, types: false)});
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
