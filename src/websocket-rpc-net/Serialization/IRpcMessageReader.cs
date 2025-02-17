using System.Buffers;

namespace Nickogl.WebSockets.Rpc.Serialization;

/// <summary>
/// Read methods and their parameters from RPC messages.
/// </summary>
public interface IRpcMessageReader : IDisposable
{
	/// <summary>Get a buffer to write the received RPC message into.</summary>
	IBufferWriter<byte> ReceiveBuffer { get; }

	/// <summary>Whether the end of the RPC message was reached.</summary>
	bool EndOfMessage { get; }

	/// <summary>Get a reader for the current RPC parameter.</summary>
	/// <remarks>Must only use after a call to <see cref="BeginReadParameter"/>.</remarks>
	IRpcParameterReader ParameterReader { get; }

	/// <summary>
	/// Reset the reader so that it can be re-used later.
	/// </summary>
	void Reset();

	/// <summary>
	/// Read a method key from the RPC message.
	/// </summary>
	/// <returns>A positive number denoting the RPC method to call.</returns>
	int ReadMethodKey();

	/// <summary>
	/// Begin reading a parameter from the RPC message through <see cref="ParameterReader"/>.
	/// </summary>
	/// <remarks>Must have a following call to <see cref="EndReadParameter"/>.</remarks>
	void BeginReadParameter();

	/// <summary>
	/// End reading a parameter from the RPC message.
	/// </summary>
	/// <remarks>Must have a preceding call to <see cref="BeginReadParameter"/>.</remarks>
	void EndReadParameter();
}
