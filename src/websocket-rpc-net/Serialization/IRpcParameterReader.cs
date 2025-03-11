namespace Nickogl.WebSockets.Rpc.Serialization;

/// <summary>
/// Read parameter data from an RPC message.
/// </summary>
public interface IRpcParameterReader
{
	/// <summary>Get a view over the parameter data as a <see cref="ReadOnlySpan{T}"/>.</summary>
	ReadOnlyMemory<byte> Memory { get; }

	/// <summary>Get a view over the parameter data as a <see cref="ReadOnlySpan{T}"/>.</summary>
	ReadOnlySpan<byte> Span { get; }

	/// <summary>Read parameter data using the <see cref="System.IO.Stream"/> API.</summary>
	/// <remarks>
	/// <para>
	/// The returned stream is neither writable nor seekable.
	/// </para>
	/// <para>
	/// If possible, prefer reading from <see cref="Memory"/> or
	/// <see cref="Span"/>, as they are allocation-free.
	/// </para>
	/// </remarks>
	Stream Stream { get; }
}
