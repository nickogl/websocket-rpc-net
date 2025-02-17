using System.Buffers;

namespace Nickogl.WebSockets.Rpc.Serialization;

/// <summary>
/// Configure an <see cref="RpcMessageBuffer"/>.
/// </summary>
public readonly struct RpcMessageBufferOptions
{
	/// <summary>Get the array pool to pool memory from.</summary>
	public required readonly ArrayPool<byte> Pool { get; init; }

	/// <summary>Minimum buffer size. Grows exponentially until <see cref="MaximumSize"/>.</summary>
	public required readonly int MinimumSize { get; init; }

	/// <summary>Maximum buffer size. Primarily serves as a safety mechanism.</summary>
	public required readonly int MaximumSize { get; init; }
}
