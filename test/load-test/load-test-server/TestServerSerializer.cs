using Nickogl.WebSockets.Rpc.Serialization;
using System.Buffers.Binary;

namespace Nickogl.WebSockets.Rpc.LoadTest.Server;

public class TestServerSerializer : ITestServerSerializer, ITestServerConnectionSerializer
{
	public int DeserializeInt32(IRpcParameterReader reader)
	{
		return BinaryPrimitives.ReadInt32LittleEndian(reader.Span);
	}

	public void SerializeInt32(IRpcParameterWriter writer, int parameter)
	{
		var destination = writer.GetSpan(4);
		BinaryPrimitives.WriteInt32LittleEndian(destination, parameter);
		writer.Advance(sizeof(int));
	}
}
