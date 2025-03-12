using System.Text.Json.Serialization;

namespace Nickogl.WebSockets.Rpc.IntegrationTest.Servers;

public sealed record Event1
{
	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("value")]
	public required int Value { get; init; }
}