namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// Available serialization methods for RPC parameters.
/// </summary>
public enum RpcParameterSerialization
{
	/// <summary>Use generic user-defined serialization and deserialization methods for all types of parameters.</summary>
	Generic,

	/// <summary>Use specialized user-defined serialization and deserialization methods for each type referenced in parameters.</summary>
	Specialized,

	/// <summary>Use generic auto-generated serialization and deserialization methods using <c>System.Text.Json</c>.</summary>
	JsonGeneric,

	/// <summary>Use specialized auto-generated serialization and deserialization methods using <c>System.Text.Json</c>, which are AOT-compatible.</summary>
	JsonSpecialized,
}
