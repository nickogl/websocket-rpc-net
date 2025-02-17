namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Available serialization methods for RPC parameters.
/// </summary>
public enum RpcParameterSerialization
{
	/// <summary>Use generic serialization and deserialization methods for all types of parameters.</summary>
	Generic,

	/// <summary>Use specialized serialization and deserialization methods for each type referenced in parameters.</summary>
	Specialized,
}
