namespace Nickogl.WebSockets.Rpc.Models;

/// <summary>
/// Represents a method available for being remotely called.
/// </summary>
internal readonly record struct MethodModel
{
	/// <summary>Unique numeric identifier of the method.</summary>
	public required int Key { get; init; }

	/// <summary>Human-readable name of the method.</summary>
	public required string Name { get; init; }

	/// <summary>Parameters to the method.</summary>
	public required EquatableArray<ParameterModel> Parameters { get; init; }
}
