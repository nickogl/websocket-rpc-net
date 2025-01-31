using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Nickogl.WebSockets.Rpc.Generator;
using Xunit.Abstractions;

namespace Nickogl.WebSockets.Rpc.IntegrationTest.Tests;

public class GeneratorTests(ITestOutputHelper output)
{
	private readonly ITestOutputHelper _output = output;

	[Fact]
	public void GeneratesSources()
	{
		var generator = new WebSocketRpcGenerator();
		var compilation =
			CSharpCompilation
				.Create(nameof(GeneratesSources))
				.WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
				.AddSyntaxTrees(CompilationUtils.SampleAppSyntaxTrees)
				.AddReferences(CompilationUtils.CommonReferences);
		var driver =
			CSharpGeneratorDriver
				.Create(generator)
				.RunGeneratorsAndUpdateCompilation(compilation, out _, out var _);

		var result = driver.GetRunResult();
		_output.WriteLine("Compilation diagnostics:");
		var diagnostics = compilation.GetDiagnostics();
		foreach (var diagnostic in diagnostics)
		{
			_output.WriteLine(diagnostic.GetMessage());
		}

		Assert.Single(result.Results);
		Assert.Equal(result.Results[0].GeneratedSources.Select(source => source.HintName).OrderBy(name => name),
			[
				"ChatClient.g.cs",
				"ChatServer.g.cs",
				"ChatTestClient.g.cs",
				"IChatClientSerializer.g.cs",
				"IChatClientTestSerializer.g.cs",
				"IChatServerSerializer.g.cs",
				"IChatServerTestSerializer.g.cs",
				"RpcArgMatcher.g.cs",
				"WebSocketRpcBuffer.g.cs",
				"WebSocketRpcClientAttribute.g.cs",
				"WebSocketRpcMethodAttribute.g.cs",
				"WebSocketRpcSerializationMode.g.cs",
				"WebSocketRpcServerAttribute.g.cs",
				"WebSocketRpcTestClientAttribute.g.cs",
			]);
	}
}
