using System.ComponentModel.DataAnnotations;

namespace Nickogl.WebSockets.Rpc.LoadTest.Client;

internal sealed class TestOptions : IValidatableObject
{
	public sealed class Node
	{
		[Url]
		public string BaseUrl { get; set; } = string.Empty;
	}

	/// <summary>For how long to run the load test.</summary>
	public TimeSpan TestDuration { get; set; } = TimeSpan.FromMinutes(1);

	/// <summary>URL to the test server. Must be set for nodes.</summary>
	[Url]
	public string? TestServerUrl { get; set; }

	/// <summary>How many concurrent connections to open to the test server at <see cref="TestServerUrl"/>.</summary>
	[Range(1, 32768)]
	public int Connections { get; set; } = 1000;

	/// <summary>Maximum time to wait for ramping down the load test.</summary>
	public TimeSpan MaximumRampdownDuration { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>Maximum concurrency when connecting to the test server.</summary>
	public int MaximumConcurrentConnects { get; set; } = Environment.ProcessorCount;

	/// <summary>Nodes to run load tests and to collect results from.</summary>
	/// <remarks>This list must be empty for nodes and contain at least one node for the master.</remarks>
	public List<Node> Nodes { get; set; } = [];

	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		var results = new List<ValidationResult>();
		if (string.IsNullOrEmpty(TestServerUrl) && Nodes.Count == 0)
		{
			results.Add(new("Must set TestServerUrl for a test node"));
		}
		foreach (var node in Nodes)
		{
			Validator.TryValidateObject(node, new(node), results);
		}
		return results;
	}
}
