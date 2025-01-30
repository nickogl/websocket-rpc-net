
/**
 * @typedef {import('./chatClientSerializerBase.js').ChatClientSerializerBase} ChatClientSerializerBase
 */

class ChatClientBase
{
	/**
	 * Interval in which to ping the websocket-rpc server. This value should be
	 * lower than the interval the server expects to account for latency.
	 *
	 * Set this to null (which is the default) to not ping the server.
	 *
	 * @type {number | null}
	 */
	pingIntervalMs;

	/**
	 * Maximum time to wait for the initial ping message from the server after the
	 * websocket connection has been established. Defaults to 5 seconds.
	 *
	 * @type {number}
	 */
	connectionTimeoutMs;

	/**
	 * Underlying websocket of this client.
	 *
	 * @type {WebSocket | null}
	 */
	webSocket;

	/** @type {string} */
	#url;

	/** @type {number | undefined} */
	#pingTaskId;

	/** @type {null | () => void} */
	#resolveConnectPromise;

	/** @type {number | undefined} */
	#awaitConnectionTaskId;

	/** @type {ChatClientSerializerBase} */
	#serializer;
	
	/**
	 * Create a new client for a websocket-rpc server.
	 *
	 * @param {string} url URL of the websocket-rpc server to connect to later.
	 * @param {ChatClientSerializerBase | undefined } serializer Serializer to serialize and deserialize RPC parameters, if needed
	 */
	constructor(url, serializer) {
		if (this.constructor == ChatClientBase) {
			throw new Error('Must not instantiate abstract class "ChatClientBase"');
		}

		if (serializer == null) {
			throw new Error('Must provide a serializer since there are RPC parameters');
		}
		this.#serializer = serializer;

		this.pingIntervalMs = null;
		this.connectionTimeoutMs = 5000;
		this.webSocket = null;
		this.#url = url;
		this.#resolveConnectPromise = null;
	}

	/**
	 * Connect to the websocket-rpc server.
	 *
	 * @throws {Error} Already connected or could not connect to the server.
	 * @returns {Promise} A promise that resolves once the client has successfully connected. RPC messages may be sent from this point onward.
	 */
	connect() {
		return new Promise((resolve, reject) => {
			if (this.webSocket !== null) {
				reject(new Error('Already connected'));
			}

			try {
				this.#resolveConnectPromise = () => {
					this.#awaitConnectionTaskId = null;
					this.#resolveConnectPromise = null;
					resolve();
				};

				this.webSocket = new WebSocket(this.#url);
				this.webSocket.binaryType = 'arraybuffer';
				this.webSocket.onopen = () => {
					this.webSocket.onerror = () => this.onError();
					this.#awaitConnectionTaskId = setTimeout(() => {
						if (this.#resolveConnectPromise !== null) {
							reject(new Error('Unable to connect'));
						}
					}, this.connectionTimeoutMs);
				}
				this.webSocket.onmessage = event => this.__onMessage(event);
				this.webSocket.onerror = () => {
					this.onError(); reject(new Error('Unable to connect'));
				};
				this.webSocket.onclose = () => {
					if (this.#awaitConnectionTaskId === null) {
						this.onDisconnected();
					}
				}
			} catch (e) {
				clearTimeout(this.#awaitConnectionTaskId);
				clearInterval(this.#pingTaskId);
				this.webSocket = null;
				reject(e);
			}
		});
	}

	/**
	 * Disconnect from the websocket-rpc server.
	 *
	 * @throws {Error} Not yet connected or there was an error while disconnecting.
	 * @returns {Promise} A promise that resolves once the client has successfully disconnected.
	 */
	disconnect() {
		return new Promise((resolve, reject) => {
			if (this.webSocket !== null) {
				reject(new Error('Not yet connected'));
			}

			try {
				this.webSocket.onclose = () => { this.onDisconnected(); resolve(); };
				this.webSocket.close();
			} catch (e) {
				reject(e);
			}
		});
	}

	onConnected() {
		if (this.pingIntervalMs !== null) {
			clearInterval(this.#pingTaskId);
			this.#pingTaskId = setInterval(() => this.__ping(), this.pingIntervalMs);
		}
	}

	onDisconnected() {
		clearInterval(this.#pingTaskId);
		this.webSocket = null;
	}

	/**
	 * Does not do anything by default. You may override this to e.g. add re-connection logic.
	 *
	 * @param {Error | undefined} error Raised error, undefined if unknown
	 */
	onError(error) { }

	/**
	 * @abstract
	 * @param message Parameter of .NET type 'System.String' on the server
	 */
	onPostMessage(message) { throw new Error('Must implement abstract method "onPostMessage"'); }

	/**
	 * Call 'PostMessage' (key: 1) on the server.
	 *
	 * @param message Parameter of .NET type 'System.String' on the server
	 */
	postMessage(message) {
		var __data = new Uint8Array(4);
		const __view = new DataView(__data.buffer);
		__view.setInt32(0, 1, true);
		this.webSocket.send(__data);

		const __message__ = this.#serializer.serializeSystemString(message);
		__view.setInt32(0, __message__.byteLength, true);
		this.webSocket.send(__data);
		this.webSocket.send(__message__);
	}

	__ping() {
		const data = new Uint8Array(4);
		const view = new DataView(data.buffer);
		view.setInt32(0, 0, true);

		this.webSocket.send(data.buffer);
	}

	/** @param {MessageEvent} event WebSocket message sent by the server */
	__onMessage(__event) {
		try {
			const __dataView = new DataView(__event.data);
			let __currentOffset = 0;
			while (__currentOffset < __dataView.byteLength) {
				const __methodKey = __dataView.getInt32(__currentOffset, true);
				__currentOffset += 4;
				switch (__methodKey) {
					case 0:
						if (this.#resolveConnectPromise !== null) {
	 						this.onConnected();
							this.#resolveConnectPromise();
							break;
						}
					case 1:
						var __messageLength__ = __dataView.getUint32(__currentOffset, true);
						__currentOffset += 4;
						var message = this.#serializer.deserializeSystemString(new Uint8Array(__event.data, __currentOffset, __messageLength__));
						__currentOffset += __messageLength__;
						this.onPostMessage(message);
						break;
					default:
						throw new Error(`Invalid method key: ${__methodKey}`);
				}
			}
		} catch (e) {
			this.onError(e);
		}
	}
}