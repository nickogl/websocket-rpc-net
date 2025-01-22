

using System.Text;

namespace SampleApp;

public sealed class ChatSerializer : IChatServerSerializer, IChatClientSerializer
{
	public string DeserializeSystemString(ReadOnlySpan<byte> data)
	{
		return Encoding.UTF8.GetString(data);
	}

	public ReadOnlySpan<byte> SerializeSystemString(string data)
	{
		return Encoding.UTF8.GetBytes(data);
	}
}
