
class ChatClientSerializerBase
{
	constructor() {
		if (this.constructor == ChatClientSerializerBase) {
			throw new Error('Must not instantiate abstract class "ChatClientSerializerBase"');
		}
	}

	/**
	 * Deserialize data into the equivalent of the .NET type 'System.String' on the server.
	 *
	 * @abstract
	 * @param {Uint8Array} data Raw data to deserialize
	 * @returns {any} Deserialized object
	 */
	deserializeSystemString(data) { throw new Error('Must implement abstract method "deserializeSystemString"'); }

	/**
	 * Serialize data into the equivalent of the .NET type 'System.String' on the server.
	 *
	 * @abstract
	 * @param {any} obj Object to serialize
	 * @returns {Uint8Array} Serialized data
	 */
	serializeSystemString(obj) { throw new Error('Must implement abstract method "serializeSystemString"'); }
}