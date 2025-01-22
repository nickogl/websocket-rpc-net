namespace Nickogl.WebSockets.Rpc.Models;

/// <summary>
/// Represents a parameter passed to an RPC method.
/// </summary>
internal readonly record struct ParameterModel
{
	/// <summary>Fully-qualified type of the parameter.</summary>
	public required string Type { get; init; }

	/// <summary>Name of the parameter.</summary>
	public required string Name { get; init; }
}
