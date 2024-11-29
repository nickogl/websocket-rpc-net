
namespace SampleApp;

public partial class ChatServer : IChatServer
{
	public ValueTask PostMessage(IChatClient client, string message)
		=> throw new NotImplementedException();
}
