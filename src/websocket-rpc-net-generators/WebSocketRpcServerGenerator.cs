using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
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
			!TryExtractClientSymbol(serverSymbol, out var clientSymbol) ||
			!clientSymbol.Name.StartsWith("I") ||
			!TryExtractServerMetadata(serverSymbol, out var metadata) ||
			!TryExtractMethods(serverSymbol, requiredFirstParameter: clientSymbol, out var serverMethods) ||
			!TryExtractMethods(clientSymbol, requiredFirstParameter: null, out var clientMethods))
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

	private static bool TryExtractServerMetadata(INamedTypeSymbol interfaceSymbol, out WebSocketRpcServerBlueprint.ServerMetadata metadata)
	{
		metadata = default;

		foreach (var attribute in interfaceSymbol.GetAttributes())
		{
			if (attribute.AttributeClass?.ToDisplayString() != "Nickogl.WebSockets.Rpc.Attributes.WebSocketRpcServerAttribute")
			{
				continue;
			}

			WebSocketRpcServerBlueprint.ParameterSerializationMode? serializationMode = null;
			foreach (var args in attribute.NamedArguments)
			{
				if (args.Key == "ParameterSerializationMode")
				{
					if (args.Value.Value is int mode)
					{
						serializationMode = (WebSocketRpcServerBlueprint.ParameterSerializationMode)mode;
					}
					else
					{
						// Invalid value, abort source generation
						return false;
					}
				}
			}
			if (serializationMode == null)
			{
				// Invalid attribute usage, abort source generation
				return false;
			}

			metadata = new WebSocketRpcServerBlueprint.ServerMetadata(serializationMode.Value);
			return true;
		}

		// No server attribute found, abort source generation
		return false;
	}

	private static bool TryExtractMethods(INamedTypeSymbol interfaceSymbol, INamedTypeSymbol? requiredFirstParameter, out List<WebSocketRpcServerBlueprint.Method> methods)
	{
		methods = [];

		foreach (var member in interfaceSymbol.GetMembers())
		{
			if (member is not IMethodSymbol methodSymbol)
			{
				continue;
			}

			WebSocketRpcServerBlueprint.MethodMetadata? metadata = null;
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
				metadata = new WebSocketRpcServerBlueprint.MethodMetadata(methodKey);
			}
			if (metadata == null)
			{
				// Unrelated interface method, skip
				continue;
			}
			if (methodSymbol.TypeParameters.Length > 0)
			{
				// Generic RPC methods not supported, abort source generation
				return false;
			}
			if (requiredFirstParameter != null && methodSymbol.Parameters.Length == 0)
			{
				// Server method must have at least one parameter taking the client calling it, abort source generation
				return false;
			}

			WebSocketRpcServerBlueprint.MethodReturnType returnType;
			if (methodSymbol.ReturnsVoid)
			{
				returnType = WebSocketRpcServerBlueprint.MethodReturnType.Void;
			}
			else if (methodSymbol.ReturnType.Name == typeof(ValueTask).Name) // netstandard2.0 does not include ValueTask
			{
				returnType = WebSocketRpcServerBlueprint.MethodReturnType.ValueTask;
			}
			else if (methodSymbol.ReturnType.ToDisplayString() == typeof(Task).FullName)
			{
				returnType = WebSocketRpcServerBlueprint.MethodReturnType.Task;
			}
			else
			{
				// Unsupported return type, abort source generation
				return false;
			}

			var parameterTypes = new List<string>(capacity: methodSymbol.Parameters.Length);
			for (int i = 0; i < methodSymbol.Parameters.Length; i++)
			{
				// TODO: support validation through data annotations?
				var fullyQualifiedTypeName = methodSymbol.Parameters[i].Type.ToDisplayString();
				if (i == 0 && requiredFirstParameter != null)
				{
					if (requiredFirstParameter?.ToDisplayString() != fullyQualifiedTypeName)
					{
						// First parameter of a server method must be the client calling it, abort source generation
						return false;
					}
				}
				else
				{
					parameterTypes.Add(fullyQualifiedTypeName);
				}
			}

			methods.Add(new WebSocketRpcServerBlueprint.Method(methodSymbol.Name, returnType, parameterTypes, metadata.Value));
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
