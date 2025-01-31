using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	internal static string GetEscapedParameterType(string type)
	{
		var escaped = new StringBuilder(capacity: type.Length);
		for (int i = 0; i < type.Length; i++)
		{
			var ch = type[i];
			if (char.IsLetter(ch) || char.IsDigit(ch))
			{
				escaped.Append(ch);
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
		void AddNamespaceAndType()
		{
			type.Append(GetFullyQualifiedNamespace(typeSymbol.ContainingNamespace));
			type.Append('.');
			type.Append(typeSymbol.Name);
		}

		if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
		{
			if (namedTypeSymbol.IsTupleType && namedTypeSymbol.TupleElements.Length > 0)
			{
				type.Append("(");
				type.Append(string.Join(", ", namedTypeSymbol.TupleElements.Select(f => $"{GetFullyQualifiedType(f.Type)} {f.Name}")));
				type.Append(")");
			}
			else if (namedTypeSymbol.IsGenericType && namedTypeSymbol.TypeArguments.Length > 0)
			{
				AddNamespaceAndType();
				type.Append("<");
				type.Append(string.Join(", ", namedTypeSymbol.TypeArguments.Select(GetFullyQualifiedType)));
				type.Append(">");
			}
			else
			{
				AddNamespaceAndType();
			}
		}
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
				//   1. Method must be public and not be generic nor static
				//   2. Method must return a ValueTask
				//   3. Method must not contain out or ref parameters
				//   4. Method must have at least one parameter in a server context
				//   5. Method must have the client type as its first parameter in a server context
				//   6. Attribute must be constructed with a positive 32-bit integer representing the method key
				//   7. Method key must be unique within the class
				//
				if (methodSymbol.DeclaredAccessibility != Accessibility.Public || methodSymbol.IsGenericMethod || methodSymbol.IsStatic ||
					// In a client context, the method must also be partial, but this information is lost when referencing it from another project
					// Therefore we should only check this in a future analyzer whose scope is restricted to the current project
					methodSymbol.ReturnType.Name != typeof(ValueTask).Name ||
					methodSymbol.Parameters.Any(param => param.RefKind != RefKind.None) ||
					(firstParameterType != null && methodSymbol.Parameters.Length == 0) ||
					// We cannot use SymbolEqualityComparer because deems clients unequal when referencing them from another project
					(firstParameterType != null && GetFullyQualifiedType(firstParameterType) != GetFullyQualifiedType(methodSymbol.Parameters[0].Type)) ||
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

		methods.Sort((x, y) => x.Key.CompareTo(y.Key));
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
