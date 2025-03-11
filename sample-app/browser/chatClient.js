import { ChatClientSerializerBase } from './generated/chatClientSerializerBase.js'
import { ChatClientBase } from './generated/chatClientBase.js';

export class ChatClientSerializer extends ChatClientSerializerBase {
  deserializeString(data) {
    return new TextDecoder().decode(data);
  }

  serializeString(obj) {
    return new TextEncoder().encode(obj);
  }
}

export class ChatClient extends ChatClientBase {
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
