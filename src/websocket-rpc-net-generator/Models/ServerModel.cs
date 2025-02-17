namespace Nickogl.WebSockets.Rpc.Models;

/// <summary>
/// Represents a websocket-rpc server to receive calls from clients.
/// </summary>
internal readonly record struct ServerModel
{
	/// <summary>Class representation of the server.</summary>
	public required ClassModel Class { get; init; }

	/// <summary>Namespace of the client class connecting to the server.</summary>
	public required string ClientClassNamespace { get; init; }

	/// <summary>Name of the client class connecting to the server.</summary>
	public required string ClientClassName { get; init; }

	/// <summary>Serializer for all RPC parameter types supported by the server. Null if there are no RPC parameters.</summary>
	public required SerializerModel? Serializer { get; init; }
}
