namespace Nickogl.WebSockets.Rpc.Models;

/// <summary>
/// Represents a websocket-rpc client to receive calls from servers.
/// </summary>
internal readonly record struct ClientModel
{
	/// <summary>Class representation of the client.</summary>
	public required ClassModel Class { get; init; }

	/// <summary>Serializer for all RPC parameter types supported by the client. Null if there are no RPC parameters.</summary>
	public required SerializerModel? Serializer { get; init; }
}
