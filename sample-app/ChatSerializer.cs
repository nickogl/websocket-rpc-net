using Nickogl.WebSockets.Rpc.Serialization;
using System.Text;

namespace SampleApp;

internal class ChatSerializer : IChatServerSerializer, IChatClientSerializer
{
	public string DeserializeString(IRpcParameterReader reader)
	{
		return Encoding.UTF8.GetString(reader.Span);
	}

	public void SerializeString(IRpcParameterWriter writer, string parameter)
	{
		var destination = writer.GetSpan(Encoding.UTF8.GetByteCount(parameter));
		writer.Advance(Encoding.UTF8.GetBytes(parameter, destination));
	}
}
