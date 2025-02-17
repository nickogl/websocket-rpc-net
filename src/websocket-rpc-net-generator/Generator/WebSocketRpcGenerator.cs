using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nickogl.WebSockets.Rpc.Generator;

[Generator]
public sealed partial class WebSocketRpcGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		context.RegisterPostInitializationOutput(GeneratePredefined);

		var servers = context.SyntaxProvider
				.CreateSyntaxProvider(IsServerOrClientCandidate, ExtractServerModel)
				.Where(model => model is not null);
		context.RegisterSourceOutput(servers,
				(context, model) => GenerateServerClass(context, model!.Value));

		var clients = context.SyntaxProvider
				.CreateSyntaxProvider(IsServerOrClientCandidate, ExtractClientModel)
				.Where(model => model is not null);
		context.RegisterSourceOutput(clients,
				(context, model) => GenerateClientClass(context, model!.Value));

		var testClients = context.SyntaxProvider
				.CreateSyntaxProvider(IsServerOrClientCandidate, ExtractTestClientModel)
				.Where(model => model is not null);
		context.RegisterSourceOutput(testClients,
				(context, model) => GenerateTestClientClass(context, model!.Value));
	}

	internal static bool IsServerOrClientCandidate(SyntaxNode node, CancellationToken cancellationToken)
	{
		return node is ClassDeclarationSyntax classNode &&
			classNode.AttributeLists.Count > 0 &&
			classNode.Modifiers.Any(SyntaxKind.PartialKeyword);
	}
}
