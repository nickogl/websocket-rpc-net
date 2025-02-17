namespace Nickogl.WebSockets.Rpc.Models;

internal readonly record struct TestClientModel
{
	/// <summary>Namespace of the test client class.</summary>
	public required string ClassNamespace { get; init; }

	/// <summary>Name of the test client class.</summary>
	public required string ClassName { get; init; }

	/// <summary>Server class under test.</summary>
	public required ClassModel ServerClass { get; init; }

	/// <summary>Client class under test.</summary>
	public required ClassModel ClientClass { get; init; }

	/// <summary>Serializer for all RPC parameter types supported by the server. Null if there are no RPC parameters.</summary>
	public required SerializerModel? ServerSerializer { get; init; }

	/// <summary>Serializer for all RPC parameter types supported by the client. Null if there are no RPC parameters.</summary>
	public required SerializerModel? ClientSerializer { get; init; }
}
