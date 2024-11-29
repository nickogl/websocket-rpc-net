namespace Nickogl.WebSockets.Rpc.Attributes;

/// <summary>
/// Serialization modes for websocket-rpc method parameters.
/// </summary>
public enum ParameterSerializationMode
{
	/// <summary>Generate a generic serialization and deserialization method for all types of parameters.</summary>
	Generic,

	/// <summary>Generate specialized serialization and deserialization methods for each type referenced in parameters.</summary>
	Specialized,
}
