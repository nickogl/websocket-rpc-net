using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Nickogl.WebSockets.Rpc.Generator;
using System.CommandLine;
using System.Text;

var sourceDirectory = new Option<DirectoryInfo>("--source", "Directory of the C# source code generate client from");
var outputDirectory = new Option<DirectoryInfo>("--output", "Directory to put generated JavaScript sources");
var root = new RootCommand("Generate a websocket-rpc client for the browser") { sourceDirectory, outputDirectory };
root.SetHandler((source, output) =>
{
	var sourceFiles = source.GetFiles("*.cs", SearchOption.AllDirectories);
	var generator = new RpcClientGenerator();
	var compilation =
		CSharpCompilation
			.Create("client")
			.WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
			.AddSyntaxTrees(sourceFiles.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file.FullName))))
			.AddReferences(ReferenceAssemblies.NetStandard20);
	var driver =
		CSharpGeneratorDriver
			.Create(generator)
			.RunGeneratorsAndUpdateCompilation(compilation, out _, out var _);
	var driverRunResult = driver.GetRunResult();
	if (driverRunResult.Results.Length == 0)
	{
		Console.Error.WriteLine("Failed to generate client sources.\n\nDiagnostics:");
		foreach (var diagnostic in compilation.GetDiagnostics())
		{
			Console.Error.WriteLine(diagnostic);
		}
		Environment.Exit(1);
	}

	output.Create();
	foreach (var result in driverRunResult.Results)
	{
		foreach (var generatedSource in result.GeneratedSources)
		{
			var outputFileName = generatedSource.HintName.ToCharArray().AsSpan();
			outputFileName[0] = char.ToLower(outputFileName[0]);
			if (outputFileName.EndsWith(['.', 'c', 's']))
			{
				outputFileName = outputFileName[..(outputFileName.Length - 3)];
			}

			var outputFilePath = Path.Combine(output.FullName, new string(outputFileName));
			using var outputWriter = new StreamWriter(outputFilePath, Encoding.UTF8, new() { Mode = FileMode.Create, Access = FileAccess.Write });
			generatedSource.SourceText.Write(outputWriter);
		}
	}
}, sourceDirectory, outputDirectory);
root.Invoke(args);
