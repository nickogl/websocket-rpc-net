using System.Buffers.Binary;

namespace Nickogl.WebSockets.Rpc.LoadTest.App;

public class TestHubSerializer : ITestHubSerializer, ITestHubConnectionSerializer
{
	public int DeserializeSystemInt32(ReadOnlySpan<byte> data)
	{
		return BinaryPrimitives.ReadInt32LittleEndian(data);
	}

	public ReadOnlySpan<byte> SerializeSystemInt32(int data)
	{
		var result = new byte[4];
		BinaryPrimitives.WriteInt32LittleEndian(result, data);
		return result;
	}
}
