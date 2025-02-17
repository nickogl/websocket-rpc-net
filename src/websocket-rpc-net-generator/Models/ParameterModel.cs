namespace Nickogl.WebSockets.Rpc.Models;

/// <summary>
/// Represents a parameter passed to an RPC method.
/// </summary>
internal readonly record struct ParameterModel
{
	/// <summary>Type of the parameter.</summary>
	public required ParameterTypeModel Type { get; init; }

	/// <summary>Name of the parameter.</summary>
	public required string Name { get; init; }
}
