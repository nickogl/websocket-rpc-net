namespace Nickogl.WebSockets.Rpc.Serialization;

/// <summary>
/// Read parameter data from an RPC message.
/// </summary>
public interface IRpcParameterReader
{
	/// <summary>Get a view over the parameter data as a <see cref="ReadOnlySpan{T}"/>.</summary>
	ReadOnlyMemory<byte> ParameterMemory { get; }

	/// <summary>Get a view over the parameter data as a <see cref="ReadOnlySpan{T}"/>.</summary>
	ReadOnlySpan<byte> ParameterSpan { get; }

	/// <summary>Read parameter data using the <see cref="Stream"/> API.</summary>
	/// <remarks>
	/// <para>
	/// The returned stream is neither writable nor seekable.
	/// </para>
	/// <para>
	/// If possible, prefer reading from <see cref="ParameterMemory"/> or
	/// <see cref="ParameterSpan"/>, as they are allocation-free.
	/// </para>
	/// </remarks>
	Stream ParameterStream { get; }
}
