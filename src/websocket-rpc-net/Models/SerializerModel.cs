namespace Nickogl.WebSockets.Rpc.Models;

/// <summary>
/// Represents a serializer for RPC parameters for a client or server.
/// </summary>
internal readonly record struct SerializerModel
{
	/// <summary>Whether or not the serializer is generic, i.e. has one method for all types.</summary>
	/// <remarks>If this is <c>true</c>, <see cref="Types"/> is empty.</remarks>
	public bool IsGeneric { get; }

	/// <summary>Fully-qualified RPC parameter types the serializer needs to support.</summary>
	/// <remarks>If this is non-empty, <see cref="IsGeneric"/> is <c>false</c>.</remarks>
	public EquatableArray<string> Types { get; }

	/// <summary>Whether or not to the serializer supports serialization.</summary>
	public required bool SupportsSerialization { get; init; }

	/// <summary>Whether or not to the serializer supports deserialization.</summary>
	public required bool SupportsDeserialization { get; init; }

	/// <summary>Namespace of the serializer interface.</summary>
	public required string InterfaceNamespace { get; init; }

	/// <summary>Name of the serializer interface.</summary>
	public required string InterfaceName { get; init; }

	public SerializerModel(bool generic, EquatableArray<string> types)
	{
		if (generic && types.Length > 0)
		{
			throw new ArgumentException("Types must be empty when creating a generic serializer model", nameof(types));
		}
		if (!generic && types.Length == 0)
		{
			throw new ArgumentException("Types must not be empty when creating a specialized serializer model", nameof(types));
		}

		IsGeneric = generic;
		Types = types;
	}
}
