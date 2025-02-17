using Nickogl.WebSockets.Rpc;
using System.Text;

namespace SampleApp;

public class ChatSerializer : IChatServerSerializer, IChatClientSerializer
{
	public string DeserializeString(IParameterReader reader)
	{
		return Encoding.UTF8.GetString(reader.Span);
	}

	public void SerializeString(IParameterWriter writer, string parameter)
	{
		var destination = writer.GetSpan(Encoding.UTF8.GetByteCount(parameter));
		Encoding.UTF8.GetBytes(parameter, destination);
	}
}
