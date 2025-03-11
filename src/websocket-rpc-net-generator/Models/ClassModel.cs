namespace Nickogl.WebSockets.Rpc.Models;

/// <summary>
/// Represents a class for either sending or receiving remote procedure calls.
/// </summary>
internal readonly record struct ClassModel
{
	/// <summary>Namespace of the class.</summary>
	public required string Namespace { get; init; }

	/// <summary>Name of the class.</summary>
	public required string Name { get; init; }

	/// <summary>Visibility of the class.</summary>
	public required string Visibility { get; init; }

	/// <summary>RPC methods of the class.</summary>
	public required EquatableArray<MethodModel> Methods { get; init; }
}
