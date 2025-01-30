/**
 * @typedef {import('./chatClientSerializerBase.js').ChatClientSerializerBase} ChatClientSerializerBase
 * @typedef {import('./chatClientBase.js').ChatClientBase} ChatClientBase
 */

class ChatClientSerializer extends ChatClientSerializerBase {
    deserializeSystemString(data) {
        return new TextDecoder().decode(data);
    }

    serializeSystemString(obj) {
        return new TextEncoder().encode(obj);
    }
}

class ChatClient extends ChatClientBase {
    constructor(url) {
        super(url, new ChatClientSerializer());
        this.pingIntervalMs = 2500;
    }

    onConnected() {
        super.onConnected();
        console.log('Connected!');
    }

    onDisconnected() {
        super.onDisconnected();
        console.log('Disconnected!');
    }

    onError(e) {
        super.onError();
        if (e !== undefined) {
            console.error(e);
        }
    }

    onPostMessage(message) {
        var li = document.createElement('li');
        li.innerText = message;
        document.getElementById('messages').appendChild(li);
    }
}
