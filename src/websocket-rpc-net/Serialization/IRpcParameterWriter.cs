using System.Buffers;

namespace Nickogl.WebSockets.Rpc.Serialization;

/// <summary>
/// Write parameter data to an RPC message.
/// </summary>
public interface IRpcParameterWriter : IBufferWriter<byte>
{
	/// <summary>Write parameter data using the <see cref="Stream"/> API.</summary>
	/// <remarks>
	/// <para>
	/// The returned stream is neither readable nor seekable.
	/// </para>
	/// <para>
	/// If possible, prefer using the <see cref="IBufferWriter{T}"/> API, as it
	/// reduces allocations to a minimum.
	/// </para>
	/// </remarks>
	Stream ParameterStream { get; }
}
