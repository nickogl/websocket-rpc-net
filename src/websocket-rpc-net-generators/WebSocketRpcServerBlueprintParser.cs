using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generators;

public static class WebSocketRpcServerBlueprintParser
{
	public static WebSocketRpcServerBlueprint? ParseBlueprint(GeneratorSyntaxContext context, CancellationToken cancellationToken)
	{
		_ = cancellationToken; // unused for now

		var serverNode = (InterfaceDeclarationSyntax)context.Node;
		if (context.SemanticModel.GetDeclaredSymbol(serverNode) is not INamedTypeSymbol serverSymbol ||
			!serverSymbol.Name.StartsWith("I") ||
			!TryExtractClientSymbol(serverSymbol, out var clientSymbol) ||
			!clientSymbol.Name.StartsWith("I") ||
			!TryExtractServerMetadata(serverSymbol, out var metadata) ||
			!TryExtractMethods(serverSymbol, requiredFirstParameter: clientSymbol, out var serverMethods, out _) ||
			!TryExtractMethods(clientSymbol, requiredFirstParameter: null, out var clientMethods, out var clientNonRpcMethods) ||
			!TryExtractProperties(clientSymbol, out var clientProperties))
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
			clientMethods,
			clientProperties,
			clientNonRpcMethods);
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

			var serializationMode = WebSocketRpcServerBlueprint.ParameterSerializationMode.Generic;
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

			metadata = new WebSocketRpcServerBlueprint.ServerMetadata(serializationMode);
			return true;
		}

		// No server attribute found, abort source generation
		return false;
	}

	private static bool TryExtractMethods(INamedTypeSymbol interfaceSymbol, INamedTypeSymbol? requiredFirstParameter, out List<WebSocketRpcServerBlueprint.Method> methods, out List<string> nonRpcMethods)
	{
		methods = [];
		nonRpcMethods = [];

		foreach (var member in interfaceSymbol.GetMembers())
		{
			if (member is not IMethodSymbol methodSymbol)
			{
				continue;
			}

			WebSocketRpcServerBlueprint.MethodMetadata? metadata = null;
			var methodKeys = new HashSet<int>();
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
				if (!methodKeys.Add(methodKey))
				{
					// Duplicate method key, abort source generation
					return false;
				}
				metadata = new WebSocketRpcServerBlueprint.MethodMetadata(methodKey);
			}
			if (metadata == null)
			{
				if (methodSymbol.MethodKind == MethodKind.Ordinary)
				{
					nonRpcMethods.Add(GetMethodDeclaration(methodSymbol));
				}
				continue;
			}
			if (methodSymbol.IsGenericMethod)
			{
				// Generic RPC methods not supported, abort source generation
				return false;
			}
			if (methodSymbol.Parameters.Any(param => param.RefKind != RefKind.None))
			{
				// Contains out or ref parameters, abort source generation
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
			if (requiredFirstParameter == null && returnType != WebSocketRpcServerBlueprint.MethodReturnType.ValueTask)
			{
				// Client method must return a ValueTask, abort source generation
				return false;
			}

			var parameters = new List<WebSocketRpcServerBlueprint.Parameter>(capacity: methodSymbol.Parameters.Length);
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
					parameters.Add(new WebSocketRpcServerBlueprint.Parameter(
						fullyQualifiedTypeName,
						methodSymbol.Parameters[i].Name,
						EscapeParameterType(fullyQualifiedTypeName)));
				}
			}

			methods.Add(new WebSocketRpcServerBlueprint.Method(methodSymbol.Name, returnType, parameters, metadata.Value));
		}

		// TBD: is having a unidirectional websocket a valid use case?
		return methods.Count != 0;
	}

	private static bool TryExtractProperties(INamedTypeSymbol interfaceSymbol, out List<WebSocketRpcServerBlueprint.Property> properties)
	{
		properties = [];

		foreach (var member in interfaceSymbol.GetMembers())
		{
			if (member is not IPropertySymbol propertySymbol)
			{
				continue;
			}
			if (propertySymbol.IsIndexer)
			{
				// Indexers not supported, abort source generation
				return false;
			}

			properties.Add(new WebSocketRpcServerBlueprint.Property(
				type: propertySymbol.Type.ToDisplayString(),
				name: propertySymbol.Name,
				isReadOnly: propertySymbol.IsReadOnly));
		}

		return true;
	}

	private static string EscapeParameterType(string type)
	{
		var escaped = new StringBuilder(capacity: type.Length);
		for (int i = 0; i < type.Length; i++)
		{
			var ch = type[i];
			if (ch == '<')
			{
				escaped.Append(char.ToUpperInvariant(type[++i]));
			}
			else if (ch != '.' && ch != '>')
			{
				escaped.Append(i == 0 ? char.ToUpperInvariant(ch) : ch);
			}
		}
		return escaped.ToString();
	}

	private static string GetMethodDeclaration(IMethodSymbol symbol)
	{
		return $"{symbol.ReturnType.ToDisplayString()} {symbol.Name}({string.Join(", ", symbol.Parameters.Select(param => $"{param.Type.ToDisplayString()} {param.Name}"))})";
	}
}
