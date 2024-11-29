using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Nickogl.WebSockets.Rpc.Generators;
using System.Runtime.CompilerServices;

namespace Nickogl.WebSockets.Rpc.GeneratorTests;

public class ServerGeneratorTests
{
	[Fact]
	public void GeneratesServerClass()
	{
		var sourceDir = Path.Combine(Path.GetDirectoryName(GetThisFilePath())!, "../sample-app");
		var sourceFiles = Directory.EnumerateFiles(sourceDir, "*.cs");
		var generator = new WebSocketRpcServerGenerator();
		var compilation =
			CSharpCompilation
				.Create(nameof(GeneratesServerClass))
				.WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication))
				.AddSyntaxTrees(sourceFiles.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file))))
				.AddReferences(
					MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
					MetadataReference.CreateFromFile(typeof(IWebSocketRpcClient).Assembly.Location));
		var driver =
			CSharpGeneratorDriver
				.Create(generator)
				.RunGeneratorsAndUpdateCompilation(compilation, out _, out var _);

		var result = driver.GetRunResult();

		Assert.Single(result.Results);
		Assert.Single(result.Results[0].GeneratedSources);
	}



	private static string GetThisFilePath([CallerFilePath] string path = null!)
	{
		return path;
	}
}
