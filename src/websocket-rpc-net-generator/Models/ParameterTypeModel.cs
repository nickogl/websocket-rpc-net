namespace Nickogl.WebSockets.Rpc.Models;

/// <summary>
/// Represents a parameter type for an RPC method.
/// </summary>
internal readonly record struct ParameterTypeModel
{
	/// <summary>Fully-qualified name of the parameter type.</summary>
	public required string Name { get; init; }

	/// <summary>Escaped name of the parameter type for use in method names.</summary>
	public required string EscapedName { get; init; }

	/// <summary>Whether or not this parameter type is disposable.</summary>
	public required bool IsDisposable { get; init; }
}
