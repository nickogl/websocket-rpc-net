using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Nickogl.WebSockets.Rpc.Attributes;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generators;

[Generator]
public class WebSocketRpcServerGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var serverBlueprints =
			context.SyntaxProvider
				.CreateSyntaxProvider(IsCandidate, ExtractBlueprint)
				.Where(blueprint => blueprint is not null);
		context.RegisterSourceOutput(
			serverBlueprints,
			(context, blueprint) => Generate(context, blueprint!.Value));
	}

	private static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken)
	{
		return node is InterfaceDeclarationSyntax interfaceNode &&
			interfaceNode.AttributeLists.Count > 0 &&
			interfaceNode.BaseList != null &&
			interfaceNode.BaseList.Types.Count > 0;
	}

	private static WebSocketRpcServerBlueprint? ExtractBlueprint(GeneratorSyntaxContext context, CancellationToken cancellationToken)
	{
		var serverNode = (InterfaceDeclarationSyntax)context.Node;
		if (context.SemanticModel.GetDeclaredSymbol(serverNode) is not INamedTypeSymbol serverSymbol ||
			!serverSymbol.Name.StartsWith("I") ||
			!TryExtractServerMetadata(serverSymbol, out var metadata) ||
			!TryExtractMethods(serverSymbol, out var serverMethods) ||
			!TryExtractClientSymbol(serverSymbol, out var clientSymbol) ||
			!clientSymbol.Name.StartsWith("I") ||
			!TryExtractMethods(clientSymbol, out var clientMethods))
		{
			return null;
		}

		return new WebSocketRpcServerBlueprint(
			metadata,
			serverSymbol.ContainingNamespace.ToDisplayString(),
			serverSymbol.Name,
			serverMethods,
			clientSymbol.ContainingNamespace.ToDisplayString(),
			clientSymbol.Name,
			clientMethods);
	}

	private static bool TryExtractClientSymbol(INamedTypeSymbol interfaceSymbol, out INamedTypeSymbol result)
	{
		result = null!;

		foreach (var serverCandidate in interfaceSymbol.AllInterfaces)
		{
			if (serverCandidate.ConstructedFrom.ToDisplayString() != "Nickogl.WebSockets.Rpc.IWebSocketRpcServer<T>" ||
				serverCandidate.TypeArguments.Length != 1 ||
				serverCandidate.TypeArguments[0] is not INamedTypeSymbol clientSymbol)
			{
				continue;
			}

			foreach (var clientCandidate in clientSymbol.AllInterfaces)
			{
				if (clientCandidate.ToDisplayString() == "Nickogl.WebSockets.Rpc.IWebSocketRpcClient")
				{
					result = clientSymbol;
					return true;
				}
			}

			// Server interface implementation found but the client type is wrong, abort source generation
			return false;
		}

		// No server interface implementation found, abort source generation
		return false;
	}

	private static bool TryExtractServerMetadata(INamedTypeSymbol interfaceSymbol, out WebSocketRpcServerAttribute metadata)
	{
		metadata = new WebSocketRpcServerAttribute();

		foreach (var attribute in interfaceSymbol.GetAttributes())
		{
			if (attribute.AttributeClass?.ToDisplayString() != "Nickogl.WebSockets.Rpc.Attributes.WebSocketRpcServerAttribute")
			{
				continue;
			}

			foreach (var args in attribute.NamedArguments)
			{
				if (args.Key == nameof(WebSocketRpcServerAttribute.ParameterSerializationMode))
				{
					if (args.Value.Value is ParameterSerializationMode parameterSerializationMode)
					{
						metadata.ParameterSerializationMode = parameterSerializationMode;
					}
					else
					{
						// Invalid value, abort source generation
						return false;
					}
				}
			}
			return true;
		}

		// No server attribute found, abort source generation
		return false;
	}

	private static bool TryExtractMethods(INamedTypeSymbol interfaceSymbol, out IReadOnlyCollection<WebSocketRpcServerBlueprint.Method> methods)
	{
		methods = [];

		foreach (var member in interfaceSymbol.GetMembers())
		{
			if (member is not IMethodSymbol methodSymbol)
			{
				continue;
			}

			foreach (var attribute in methodSymbol.GetAttributes())
			{
				if (attribute.AttributeClass?.ToDisplayString() != "Nickogl.WebSockets.Rpc.Attributes.WebSocketRpcMethodAttribute")
				{
					continue;
				}
				if (attribute.ConstructorArguments.Length != 1 ||
					attribute.ConstructorArguments[0].Value is not int methodKey ||
					methodKey <= 0)
				{
					// Invalid attribute usage, abort source generation
					return false;
				}

			}
		}

		// TBD: is having a unidirectional websocket a valid use case?
		return methods.Count != 0;
	}

	private static void Generate(SourceProductionContext context, WebSocketRpcServerBlueprint blueprint)
	{
		var serverClassName = blueprint.ServerName.TrimStart('I');
		var stringBuilder = new StringBuilder(@$"
namespace {blueprint.ServerNamespace}
{{
	public partial class {serverClassName} : Nickogl.WebSockets.Rpc.WebSocketRpcServerBase<{blueprint.ClientNamespace}.{blueprint.ClientName}>
	{{");
		// TODO
		stringBuilder.Append(@$"
	}}
}}");

		context.AddSource($"{serverClassName}.g.cs", SourceText.From(stringBuilder.ToString(), Encoding.UTF8));
	}
}
