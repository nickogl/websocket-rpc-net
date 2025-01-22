using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Runtime.CompilerServices;

namespace Nickogl.WebSockets.Rpc.IntegrationTest;

public static class CompilationUtils
{
	public readonly static IEnumerable<MetadataReference> CommonReferences = ReferenceAssemblies.NetStandard20;
	public readonly static IEnumerable<SyntaxTree> SampleAppSyntaxTrees = [];

	static CompilationUtils()
	{
		var testProjectDir = Path.GetDirectoryName(GetThisFilePath());
		if (testProjectDir != null)
		{
			var sourceDir = Path.Combine(testProjectDir, "../../sample-app");
			var sourceFiles = Directory.EnumerateFiles(sourceDir, "*.cs").Where(file => new FileInfo(file).Name != "Program.cs");
			SampleAppSyntaxTrees = [.. sourceFiles.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file)))];
		}
	}

	private static string GetThisFilePath([CallerFilePath] string path = null!)
	{
		return path;
	}
}
