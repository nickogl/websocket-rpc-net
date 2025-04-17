# Websocket RPC for .NET

A flexible library and source generator to implement RPC for websocket applications.

Compared to SignalR, it is fairly unopinionated and allows you to manage your
connections and state however you prefer. You can also choose whichever wire format
you like for your RPC parameters. The generated browser client has zero dependencies
and works with every vendor who implements the websocket specification.

## Getting started

### Backend part

First, add dependencies on the library and source generator:
```sh
dotnet add package Nickogl.WebSockets.Rpc
dotnet add package Nickogl.WebSockets.Rpc.Generator
```

Then, define the RPC client containing connection state and methods to call on the client:
```csharp
using Nickogl.WebSockets.Rpc;
using System.Net.WebSockets;

[RpcClient(RpcParameterSerialization.Specialized)]
public sealed partial class ChatHubClient(WebSocket webSocket)
{
  public override WebSocket WebSocket = webSocket;
  public int Id { get; } = Random.Shared.Next();

  [RpcMethod(1)]
  public partial ValueTask PostMessage(int clientId, string message);

  [RpcMethod(2)]
  public partial ValueTask SetActive(int clientId, bool active);
}
```
Three things to note here:
1. There are two types of parameter serialization (for now): generic and specialized.
   Generic allows you to serialize and deserialize parameters using a generic method.
   Specialized allows you to serialize and deserialize parameters using one method for
   each type of parameter encountered in your RPC methods.
2. The class must be partial. The generated counterpart instructs you to implement the
   properties `WebSocket` and `Serializer`; we'll get to the serializer stuff later.
3. Each RPC method in the client must be partial and return a `ValueTask`. The generated
   counterpart implements serializing and sending the message.

Now define the RPC server with methods to call on the server:
```csharp
using Nickogl.WebSockets.Rpc;

[RpcServer<ChatHubClient>(RpcParameterSerialization.Specialized)]
public sealed partial class ChatHub
{
  [RpcMethod(1)]
  public ValueTask PostMessage(ChatHubClient client, string message)
  {
    return ValueTask.CompletedTask;
  }
}
```
Three things to note here:
1. The class must be partial. The generated counterpart instructs you to implement the
   property `Serializer`; we'll get to that in a bit. It also allows you to tweak some
   behavior through properties and methods, but let's keep it simple for now.
2. Each RPC method in the server must return a `ValueTask` and take the client who
   called the method as its first parameter.
3. This hub does not keep track of its connections, as it cannot assume the best data
   structure for your use case and you may not even need this feature at all. Later in
   this document we will describe a way to implement this.

The source generator generated two interfaces for parameter serialization that we need to implement:
```csharp
using Nickogl.WebSockets.Rpc.Serialization;
using System.Buffers.Binary;
using System.Text;

public sealed class ChatHubSerializer : IChatHubSerializer, IChatHubClientSerializer
{
  public string DeserializeString(IRpcParameterReader reader)
  {
    return Encoding.UTF8.GetString(reader.Span);
  }

  public void SerializeString(IRpcParameterWriter writer, string parameter)
  {
    var destination = writer.GetSpan(Encoding.UTF8.GetByteCount(parameter));
    var written = Encoding.UTF8.GetBytes(parameter, destination);
    writer.Advance(written);
  }
}
```
The library pools memory (configurable) to minimize allocations, which often obliterate
performance in scenarios of high message throughputs and high connection counts. `IRpcParameterWriter`
implements `IBufferWriter<byte>` which allows you to directly write into the pooled memory.

Now that we implemented the serializer, let's use it in our RPC client and server:
```csharp
public sealed partial class ChatHubClient
{
  // Since you control instantiation of the client, you can also inject this using DI or some other mechanism.
  // If possible, using a static variable is preferred, as it saves a few bytes of memory for each connection,
  // which is not a lot but can make a difference with a very high number of connections.
  private static readonly ChatHubSerializer _serializer = new();

  protected override IChatHubClientSerializer Serializer => _serializer;

  // ...
}

public sealed partial class ChatHub
{
  // Comments above largely apply here as well. However, memory usage will not be as much of a concern here;
  // unless you organize your app in "rooms" (i.e. once hub instance per room) and you have millions of them.
  private static readonly ChatHubSerializer _serializer = new();

  protected override IChatHubSerializer Serializer => _serializer;

  // ...
}
```

Finally, instantiate a client and enter its message processing loop:
```csharp
var server = new ChatHub();
// ...
using var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
var client = new ChatHubClient(webSocket);
await server.ProcessAsync(client, CancellationToken.None);
```

### Frontend part

First, install the tool for generating the browser client:
```sh
dotnet new tool-manifest
dotnet tool install --local Nickogl.WebSockets.Rpc.Client
```

Then, generate the JavaScript base classes for parameter serialization and the RPC client:
```sh
dotnet websocket-rpc-net-client --source 'path/to/your/source' --output 'path/to/generated/sources'
```

Implement the "abstract" methods of those base classes:
```js
import { ChatHubClientSerializerBase } from './chatHubClientSerializerBase.js'
import { ChatHubClientBase } from './chatHubClientBase.js';

export class ChatHubClientSerializer extends ChatHubClientSerializerBase {
  deserializeString(data) {
    return new TextDecoder().decode(data);
  }

  serializeString(obj) {
    return new TextEncoder().encode(obj);
  }
}

export class ChatHubClient extends ChatHubClientBase {
  constructor(url) {
    super(url, new ChatHubClientSerializer());
  }

  // You are advised to put an event emitter on top of this like nanoevents (https://github.com/ai/nanoevents)
  // or eventemitter3 (https://github.com/primus/eventemitter3) to decouple your components from this class.
  onPostMessage(message) {
    ...
  }

  // Overriding this is optional, but recommended for resilience, e.g. to reconnect after a networking error.
  onError(error) {
    ...
  }
}
```

Finally, connect to the backend and perform remote procedure calls:
```js
const client = new ChatHubClient('wss://backend/url');
await client.connect(); // throws upon connection failure
client.postMessage('Hello world!');
await client.disconnect();
```

## Batching and broadcasting

The generated client supports batching multiple remote procedure calls into a single
websocket message. You can broadcast the built batches to multiple clients while
only paying for the serialization cost once.
```csharp
[RpcMethod(1)]
public async ValueTask PostMessage(ChatHubClient client, string message)
{
  using var messageWriter = new RpcMessageWriter(new RpcMessageBufferOptions()
  {
    Pool = ArrayPool<byte>.Shared,
    MinimumSize = 1024,
    MaximumSize = 1024 * 16,
  });
  client.PostMessage(messageWriter, client.Id, message);
  client.SetActive(messageWriter, client.Id, true);
  foreach (var client in _clients)
  {
    await client.TrySendAsync(messageWriter);
  }
}
```

## Client timeout

If a client abruptly disconnects or is unresponsive, it can take up to several
minutes until this is detected due to how TCP works.

Starting with .NET 9, we have `WebSocketOptions.KeepAliveTimeout` to detect client
disconnections quicker.

If you're stuck on .NET 8, you can override the `ClientTimeout` property on your
RPC server to achieve the same behavior. Note that you have to keep this value in
sync with the `pingIntervalMs` field in the browser client. `pingIntervalMs` should
be less than `ClientTimeout` and account for latency and latency spikes.

## Tweaks

### Server buffers

You can override `GetMessageReader()` to change the buffer's array pool implementation
and specify a minimum and maximum size. The maximum size should be the size of the
biggest possible legit message received by the server. The message is rejected
(and the connection closed) if its size exceeds the configured maximum. You can
also switch to another implementation of `IRpcMessageReader` entirely.

If you changed `GetMessageReader()` to pool instances, you should also override
`ReturnMessageReader()` to return those instances to the pool. Only pool message
reader instances if their allocations are confirmed to be a bottleneck.

### Client buffers

You can override `GetMessageWriter()` to change the buffer's array pool implementation
and specify a minimum and maximum size. The maximum size is just there as a safety
mechanism against unintentional behavior, but you may use `Array.MaxLength` if
you are unsure what to use.

If you changed `GetMessageWriter()` to pool instances, you should also override
`ReturnMessageWriter()` to return those instances to the pool. Only pool message
writer instances if their allocations are confirmed to be a bottleneck.

These methods are only used for the generated RPC method overloads that directly
send a single remote procedure call to the client. If you want to batch RPCs, you
should create an instance of `IRpcMessageWriter` and call the other overloads, then
use `SendAsync(messageWriter)` or `TrySendAsync(messageWriter)`.

## Tracking connections

As mentioned earlier, the source generator does not generate state and code to track
connections, as the type of data structure(s) to use highly depend on your application's
requirements. You may also need a backplane to scale out your application.

Override `OnConnectedAsync()` and `OnDisconnectedAsync()` to implement the tracking.
Here's a general purpose implementation that uses a sorted sequence, which allows
for fast iteration when e.g. broadcasting messages but still performs well enough
when adding and removing connections:
```csharp
private sealed class ChatHubClientComparer : IComparer<ChatHubClient>
{
  public int Compare(ChatHubClient? x, ChatHubClient? y)
  {
    if (x is null && y is null) return 0;
    if (x is null) return -1;
    if (y is null) return 1;

    return x.Id.CompareTo(y.Id);
  }
}

private readonly static ChatHubClientComparer _clientComparer = new();
private readonly List<ChatHubClient> _clients = [];

protected override ValueTask OnConnectedAsync(ChatHubClient client)
{
  lock (_clients)
  {
    var index = _clients.BinarySearch(client, _clientComparer);
    if (index < 0)
    {
      _clients.Insert(~index, client);
    }
  }
  return ValueTask.CompletedTask;
}

protected override ValueTask OnDisconnectedAsync(ChatHubClient client)
{
  lock (_clients)
  {
    var index = _clients.BinarySearch(client, _clientComparer);
    if (index >= 0)
    {
      _clients.RemoveAt(index);
    }
  }
  return ValueTask.CompletedTask;
}
```

## Testing

The generator package contains a source generator for RPC test clients. The
generated test client allows you to call methods on the server and verify calls
received on the client.

First, define the test client class:
```csharp
[RpcTestClient<ChatHub>]
public partial class ChatHubTestClient
{
}
```

The source generator generated two interfaces for serialization that we need to implement.
These interfaces contain the same operations as the ones generated for the application,
but reversed. Since the serialization in our example is symmetric, we can just re-use
the one we created for the application:
```csharp
public sealed class ChatHubTestSerializer : ChatHubSerializer, IChatHubTestSerializer, IChatHubClientTestSerializer
{
}
```

Finally, implement the abstract properties for the serializers:
```csharp
public sealed partial class ChatHubTestClient
{
  private readonly static ChatHubTestSerializer _serializer = new();

  protected override IChatHubTestSerializer ServerSerializer => _serializer;
  protected override IChatHubClientTestSerializer ClientSerializer => _serializer;
}
```
There are other properties and methods we can override. If you're on .NET 8 and
defined a `ClientTimeout` for the RPC server, then you'll also need to override
`PingInterval` for the RPC test client. You can override `ConfigureWebSocket()`
to send authentication headers or cookies when connecting to the RPC server. If
you're using the test client for load testing, you should override `InterceptCalls`
to be `false` to reduce memory usage.

### Usage

First you'll need to start your application. You can do this outside the test process
or host the application inside. If you want to do the latter (for easy setup and debugging),
you cannot use Microsoft's `WebApplicationFactory` as it doesn't support websockets
out of the box. You can extend it to host with kestrel yourself or simply use the
[Nickogl.AspNetCore.IntegrationTesting](https://github.com/nickogl/aspnetcore-integration-testing)
package. We'll assume the latter for this guide.
```csharp
using Nickogl.AspNetCore.IntegrationTesting;
using Nickogl.WebSockets.Rpc.Testing;

await using var app = new WebApplicationTestHost<Program>();
```

Then, instantiate two test clients and connect them to the server:
```csharp
await using var client1 = new ChatHubTestClient();
await client1.ConnectAsync(app.BaseAddress);
await using var client2 = new ChatHubTestClient();
await client2.ConnectAsync(app.BaseAddress);
```

We can now perform remote procedure calls on the server. Let's have one of the clients post a message:
```csharp
await client1.PostMessage("Hello world!");
```

Assuming that the server broadcasts this message to all clients, we now want to
assert that all clients indeed received this message:
```csharp
await client1.Received.PostMessage(RpcParameter.Any<int>(), "Hello world!");
await client2.Received.PostMessage(RpcParameter.Any<int>(), "Hello world!");
```
Things to note:
1. Each of those calls will either wait until the client receives the message or
   return immediately if it already received the message. This makes the test
   robust, as it avoids race conditions due to timings.
2. Each parameter is actually an RPC parameter matcher. `RpcParameter.Any<Type>()`
   allows you to conveniently skip parameters that you're not interested in.
   `RpcParameter.Is<Type>([unary predicate])` matches parameters that follow a
   certain pattern. Using a specific value implicitly creates a matcher that uses
   `object.Equals()` to compare the parameter (or `IEnumerable.SequenceEqual()`
   in case the parameter is a collection).
3. If no matching call is received within `ReceiveTimeout` (default: 5 seconds),
   then the test will fail and the error message will contain all calls and their
   parameters received by the client. It is advised to override `object.ToString()`
   or use records for RPC parameters to ease troubleshooting.

## TODO

- [ ] Generate JSON serializers that use System.Text.Json's serialization using reflection
- [ ] Generate AOT-compatible JSON serializers that use System.Text.Json's source generator
  - This is not possible at the moment: https://github.com/dotnet/roslyn/issues/57239
