namespace Nickogl.WebSockets.Rpc.Serialization;

/// <summary>
/// Write methods and their parameters into RPC messages.
/// </summary>
public interface IRpcMessageWriter : IDisposable
{
	/// <summary>Get the length of the message data in bytes thus far.</summary>
	int WrittenCount { get; }

	/// <summary>Get a view over the written message data thus far, as a <see cref="ReadOnlyMemory{T}"/>.</summary>
	/// <remarks>Must not use the returned view after calling <see cref="Reset"/>.</remarks>
	ReadOnlyMemory<byte> WrittenMemory { get; }

	/// <summary>Get a view over the written message data thus far, as a <see cref="ReadOnlySpan{T}"/>.</summary>
	/// <remarks>Must not use the returned view after calling <see cref="Reset"/>.</remarks>
	ReadOnlySpan<byte> WrittenSpan { get; }

	/// <summary>Get a writer for the current RPC parameter.</summary>
	/// <remarks>Must only use after a call to <see cref="BeginWriteParameter"/>.</remarks>
	IRpcParameterWriter ParameterWriter { get; }

	/// <summary>
	/// Reset the writer so that it can be re-used later.
	/// </summary>
	void Reset();

	/// <summary>Write a method key to the RPC message.</summary>
	/// <param name="key">Key of the method to write.</param>
	void WriteMethodKey(int key);

	/// <summary>
	/// Begin writing a parameter to the RPC message through <see cref="ParameterWriter"/>.
	/// </summary>
	/// <remarks>Must have a following call to <see cref="EndWriteParameter"/>.</remarks>
	void BeginWriteParameter();

	/// <summary>
	/// End writing a parameter from the RPC message.
	/// </summary>
	/// <remarks>Must have a preceding call to <see cref="BeginWriteParameter"/>.</remarks>
	void EndWriteParameter();
}
