using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	private static string GetEscapedParameterType(string type)
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

	private static string GetFullyQualifiedNamespace(INamespaceSymbol? namespaceSymbol)
	{
		if (namespaceSymbol == null)
		{
			return string.Empty;
		}

		var segments = new List<string>() { namespaceSymbol.Name };
		while (namespaceSymbol.ContainingNamespace?.IsGlobalNamespace == false)
		{
			segments.Insert(0, namespaceSymbol.ContainingNamespace.Name);
			namespaceSymbol = namespaceSymbol.ContainingNamespace;
		}
		return string.Join(".", segments);
	}

	private static string GetFullyQualifiedType(ITypeSymbol typeSymbol)
	{
		var type = new StringBuilder();
		type.Append(GetFullyQualifiedNamespace(typeSymbol.ContainingNamespace));
		type.Append('.');
		type.Append(typeSymbol.Name);
		if (typeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
		{
			type.Append('?');
		}
		return type.ToString();
	}

	private static string GetFullyQualifiedType(string @namespace, string type)
	{
		return string.IsNullOrEmpty(@namespace) ? type : $"{@namespace}.{type}";
	}

	private static ClassModel? ExtractClassModel(INamedTypeSymbol symbol, ITypeSymbol? firstParameterType = null)
	{
		var methods = new List<MethodModel>();
		foreach (var member in symbol.GetMembers())
		{
			if (member is not IMethodSymbol methodSymbol)
			{
				continue;
			}
			foreach (var attribute in methodSymbol.GetAttributes())
			{
				if (attribute.AttributeClass?.Name != "WebSocketRpcMethodAttribute")
				{
					continue;
				}

				//
				// If usage of this attribute is wrong or the method is unsuitable, we abort source generation
				//
				//   1. Method must not be generic or static
				// 	 2. Method must be public and partial in a client context, because it is implemented by the generated code
				//   3. Method must return a ValueTask
				//   4. Method must not contain out or ref parameters
				//   5. Method must have at least one parameter in a server context
				//   6. Method must have the client type as its first parameter in a server context
				//   7. Attribute must be constructed with a positive 32-bit integer representing the method key
				//   8. Method key must be unique within the class
				//
				if (methodSymbol.IsGenericMethod || methodSymbol.IsStatic ||
					(firstParameterType == null && (!methodSymbol.IsPartialDefinition || methodSymbol.DeclaredAccessibility != Accessibility.Public)) ||
					methodSymbol.ReturnType.Name != typeof(ValueTask).Name ||
					methodSymbol.Parameters.Any(param => param.RefKind != RefKind.None) ||
					(firstParameterType != null && methodSymbol.Parameters.Length == 0) ||
					(firstParameterType != null && !SymbolEqualityComparer.IncludeNullability.Equals(firstParameterType, methodSymbol.Parameters[0].Type)) ||
					attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not int methodKey || methodKey <= 0 ||
					methods.Any(method => method.Key == methodKey))
				{
					return null;
				}

				methods.Add(new MethodModel()
				{
					Key = methodKey,
					Name = methodSymbol.Name,
					Parameters = new(methodSymbol.Parameters.Skip(firstParameterType == null ? 0 : 1).Select(param =>
					{
						return new ParameterModel()
						{
							Type = GetFullyQualifiedType(param.Type),
							Name = param.Name,
						};
					}))
				});
			}
		}

		return new ClassModel()
		{
			Namespace = GetFullyQualifiedNamespace(symbol.ContainingNamespace),
			Name = symbol.Name,
			Methods = new(methods),
		};
	}

	private static SerializerModel? CreateSerializerModel(ClassModel classModel, MetadataBase metadata, bool isClient)
	{
		if (classModel.Methods.Length == 0)
		{
			return null;
		}

		var serializableTypes = metadata.UsesGenericSerialization
			? EquatableArray<string>.Empty
			: new(classModel.Methods.SelectMany(method => method.Parameters).Select(param => param.Type).Distinct());
		return new SerializerModel(metadata.UsesGenericSerialization, serializableTypes)
		{
			SupportsDeserialization = !isClient,
			SupportsSerialization = isClient,
			InterfaceNamespace = classModel.Namespace,
			InterfaceName = $"I{classModel.Name}Serializer",
		};
	}

	private abstract class MetadataBase
	{
		public required bool UsesGenericSerialization { get; init; }
	}
}
