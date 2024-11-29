using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Nickogl.WebSockets.Rpc.Generators;
using Xunit.Abstractions;

namespace Nickogl.WebSockets.Rpc.GeneratorTests;

public class ServerGeneratorTests(ITestOutputHelper output)
{
	private readonly ITestOutputHelper _output = output;

	[Fact]
	public void GeneratesServerClass()
	{
		var generator = new WebSocketRpcServerGenerator();
		var compilation =
			CSharpCompilation
				.Create(nameof(GeneratesServerClass))
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
		Assert.Single(result.Results[0].GeneratedSources);
	}
}
