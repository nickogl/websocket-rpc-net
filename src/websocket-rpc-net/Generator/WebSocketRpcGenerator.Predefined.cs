using Microsoft.CodeAnalysis;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	private static void GeneratePredefined(IncrementalGeneratorPostInitializationContext context)
	{
		context.AddSource("WebSocketRpcMethodAttribute.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_ATTRIBUTES
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Mark a method to be called by the websocket server or client. In a server
/// context, it has to additionally take the client as its first parameter. In
/// all contexts it must return a ValueTask.
/// </summary>
/// <param name=""key"">
/// Consistent, unique key of this method. Must be 1 or greater.
/// Changing this value on a method breaks existing clients.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class WebSocketRpcMethodAttribute(int key) : Attribute
{{
	/// <summary>Get the consistent, unique key of this method.</summary>
	public int Key {{ get; }} = key;
}}
#endif");

		context.AddSource("WebSocketRpcSerializationMode.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_ATTRIBUTES
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Serialization modes for RPC parameters.
/// </summary>
internal enum WebSocketRpcSerializationMode
{{
	/// <summary>Use generic serialization and deserialization methods for all types of parameters.</summary>
	Generic,

	/// <summary>Use specialized serialization and deserialization methods for each type referenced in parameters.</summary>
	Specialized,
}}
#endif");

		context.AddSource("WebSocketRpcClientAttribute.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_ATTRIBUTES
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Generate a websocket-rpc client implementation in a class. The class must
/// be partial and implement some methods for the generated code. It also has
/// to annotate all methods callable by the server with <see cref=""WebSocketRpcMethodAttribute""/>.
/// </summary>
/// <param name=""serializationMode"">Serialization mode to use for RPC parameters.</param>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class WebSocketRpcClientAttribute(WebSocketRpcSerializationMode serializationMode) : Attribute
{{
	public WebSocketRpcSerializationMode SerializationMode {{ get; }} = serializationMode;
}}
#endif");

		context.AddSource("WebSocketRpcServerAttribute.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_ATTRIBUTES
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Generate a websocket-rpc server implementation in a class. The class must
/// be partial and implement some methods for the generated code. It also has
/// to annotate all methods callable by the client with <see cref=""WebSocketRpcMethodAttribute""/>.
/// </summary>
/// <param name=""serializationMode"">Serialization mode to use for RPC parameters.</param>
/// <typeparam name=""TClient"">Type of the client connecting to the server.</typeparam>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class WebSocketRpcServerAttribute<TClient>(WebSocketRpcSerializationMode serializationMode) : Attribute
{{
	public WebSocketRpcSerializationMode SerializationMode {{ get; }} = serializationMode;
}}
#endif");

		context.AddSource("WebSocketRpcTestClientAttribute.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_ATTRIBUTES
using System;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Generate a websocket-rpc test client for calling methods on the server and
/// recording calls received from the server.
/// </summary>
/// <typeparam name=""TServer"">The websocket-rpc server type under test.</typeparam>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class WebSocketRpcTestClientAttribute<TServer> : Attribute
{{
}}
#endif");

		context.AddSource("RpcArgMatcher.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_ATTRIBUTES
using System;
using System.Collections;
using System.Linq;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Match an argument to a remote procedure call. Used to await specific RPC
/// messages in tests to ensure they are reproducible.
/// </summary>
/// <remarks>
/// Examples:
/// <para><c>await testClient.Receive.Foo(""ExactValue"")</c></para>
/// <para><c>await testClient.Receive.Foo(RpcArg.Is&lt;string&gt;(arg => arg.StartsWith(""MatchesValue"")))</c></para>
/// <para><c>await testClient.Receive.Foo(RpcArg.Any&lt;string&gt;())</c></para>
/// </remarks>
/// <typeparam name=""T"">Type of the argument to match.</typeparam>
internal readonly struct RpcArgMatcher<T>(Func<T, bool> predicate)
{{
	private readonly Func<T, bool> _predicate = predicate;

	public bool Matches(T arg)
	{{
		return _predicate(arg);
	}}

	public static implicit operator RpcArgMatcher<T>(T value)
	{{
		return new RpcArgMatcher<T>(arg =>
		{{
			if (arg == null && value == null)
			{{
				return true;
			}}
			if (arg == null || value == null)
			{{
				return false;
			}}
			if (arg is IEnumerable argList && value is IEnumerable valueList)
			{{
				object[] argArray = [.. argList];
				object[] valueArray = [.. valueList];
				return argArray.SequenceEqual(valueArray);
			}}
			return arg.Equals(value);
		}});
	}}
}}

/// <summary>
/// Helpers to create instances of <see cref=""RpcArgMatcher{{T}}"" />. Usage similar to <c>NSubstitute</c>.
/// </summary>
internal static class RpcArg
{{
	/// <summary>
	/// Create a matcher that only intercepts RPC calls if the provided predicate succeeds.
	/// </summary>
	/// <param name=""predicate"">Predicate to check the transmitted argument.</param>
	/// <typeparam name=""T"">Type of the argument to check.</typeparam>
	/// <returns>An argument matcher for use in <c>await testClient.Receive.Foo(...)</c>.</returns>
	public static RpcArgMatcher<T> Is<T>(Func<T, bool> predicate)
	{{
		return new RpcArgMatcher<T>(predicate);
	}}

	/// <summary>
	/// Create a matcher that always intercepts RPC calls for this argument.
	/// </summary>
	/// <typeparam name=""T"">Type of the argument to check.</typeparam>
	/// <returns>An argument matcher for use in <c>await testClient.Receive.Foo(...)</c>.</returns>
	public static RpcArgMatcher<T> Any<T>()
	{{
		return new RpcArgMatcher<T>(_ => true);
	}}
}}
#endif");

		context.AddSource("WebSocketRpcBuffer.g.cs", @$"
#if !WEBSOCKET_RPC_EXCLUDE_ATTRIBUTES
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Nickogl.WebSockets.Rpc;

internal struct WebSocketRpcBuffer : IDisposable
{{
	private readonly ArrayPool<byte> _allocator;
	private byte[] _buffer;
	private int _offset;
#if DEBUG
	private readonly int _maximumSize;
#endif

	public WebSocketRpcBuffer(ArrayPool<byte> allocator, int initialSize, int maximumSize)
	{{
		_allocator = allocator;
		_buffer = allocator.Rent(initialSize);
	#if DEBUG
		_maximumSize = maximumSize;
	#endif
	}}

	public void Dispose()
	{{
		_allocator.Return(_buffer);
	}}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Span<byte> AsSpan()
	{{
		return new(_buffer, 0, _offset);
	}}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Memory<byte> AsMemory()
	{{
		return new(_buffer, 0, _offset);
	}}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WriteMethodKey(int methodKey)
	{{
		EnsureAtLeast(sizeof(int));

		BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_offset, sizeof(int)), methodKey);
		_offset += sizeof(int);
	}}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WriteParameter(ReadOnlySpan<byte> data)
	{{
		EnsureAtLeast(sizeof(int) + data.Length);

		BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_offset, sizeof(int)), data.Length);
		_offset += sizeof(int);
		data.CopyTo(_buffer.AsSpan(_offset, data.Length));
		_offset += data.Length;
	}}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void EnsureAtLeast(int size)
	{{
		var requiredBufferSize = _offset + size;
#if DEBUG
		Debug.Assert(requiredBufferSize <= _maximumSize, $""Requested buffer size ({{requiredBufferSize}} bytes) exceeds maximum buffer size ({{_maximumSize}} bytes). Check if you have an infinite loop and if not, increase _maximumSize to at least {{requiredBufferSize}}."");
#endif
		var actualBufferSize = _buffer.Length;
		if (requiredBufferSize <= actualBufferSize)
		{{
			return;
		}}
		while (requiredBufferSize > actualBufferSize)
		{{
			actualBufferSize *= 2;
		}}

		var newBuffer = _allocator.Rent(actualBufferSize);
		try
		{{
			_buffer.AsSpan(0, _offset).CopyTo(newBuffer.AsSpan());
		}}
		catch (Exception)
		{{
			_allocator.Return(newBuffer);
			throw;
		}}
		_allocator.Return(_buffer);
		_buffer = newBuffer;
	}}
}}

#endif
	");
	}
}
