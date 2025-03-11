using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class RpcServerGenerator
{
	internal static string GetParameterList(IEnumerable<ParameterModel> parameters, bool types = true)
	{
		return types
			? string.Join(", ", parameters.Select(param => $"{param.Type.Name} {param.Name}"))
			: string.Join(", ", parameters.Select(param => param.Name));
	}

	private static string GetParameterMatcherList(IEnumerable<ParameterModel> parameters)
	{
		return string.Join(", ", parameters.Select(param => $"RpcParameterMatcher<{param.Type.Name}> {param.Name}"));
	}

	private static string GetEscapedParameterType(string type)
	{
		var prefix = type[type.Length - 1] == '?' ? "Nullable" : string.Empty;
		var escaped = new StringBuilder(prefix, capacity: type.Length);
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

	private static bool IsRpcMethodAttribute(AttributeData attributeData)
	{
		return attributeData.AttributeClass != null &&
			!attributeData.AttributeClass.IsGenericType &&
			attributeData.AttributeClass.ToDisplayString() == "Nickogl.WebSockets.Rpc.RpcMethodAttribute";
	}

	private static bool IsRpcServerAttribute(AttributeData attributeData)
	{
		return attributeData.AttributeClass != null &&
			attributeData.AttributeClass.IsGenericType &&
			attributeData.AttributeClass.ConstructedFrom.ToDisplayString() == "Nickogl.WebSockets.Rpc.RpcServerAttribute<TClient>";
	}

	private static bool IsRpcClientAttribute(AttributeData attributeData)
	{
		return attributeData.AttributeClass != null &&
			!attributeData.AttributeClass.IsGenericType &&
			attributeData.AttributeClass.ToDisplayString() == "Nickogl.WebSockets.Rpc.RpcClientAttribute";
	}

	private static bool IsRpcTestClientAttribute(AttributeData attributeData)
	{
		return attributeData.AttributeClass != null &&
			attributeData.AttributeClass.IsGenericType &&
			attributeData.AttributeClass.ConstructedFrom.ToDisplayString() == "Nickogl.WebSockets.Rpc.Testing.RpcTestClientAttribute<TServer>";
	}

	private static ClassModel? ExtractClassModel(INamedTypeSymbol symbol, ITypeSymbol? firstParameterType = null)
	{
		var methods = new List<MethodModel>();
		var methodKeys = new HashSet<int>();
		var methodParameterTypeCache = new Dictionary<ITypeSymbol, ParameterTypeModel>(SymbolEqualityComparer.IncludeNullability);
		var escapedParameterNames = new HashSet<string>();
		foreach (var member in symbol.GetMembers())
		{
			if (member is not IMethodSymbol methodSymbol)
			{
				continue;
			}
			foreach (var attribute in methodSymbol.GetAttributes())
			{
				if (!IsRpcMethodAttribute(attribute))
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
					!methodKeys.Add(methodKey))
				{
					return null;
				}

				methods.Add(new MethodModel()
				{
					Key = methodKey,
					Name = methodSymbol.Name,
					Parameters = new(methodSymbol.Parameters.Skip(firstParameterType == null ? 0 : 1).Select(param =>
					{
						if (!methodParameterTypeCache.TryGetValue(param.Type, out var parameterTypeModel))
						{
							var fqName = GetFullyQualifiedType(param.Type);
							var escapedName = GetEscapedParameterType(param.Type.Name);
							if (!escapedParameterNames.Add(escapedName))
							{
								// Need to disambiguate equal escaped names across multiple types
								escapedName = GetEscapedParameterType(fqName);
								escapedParameterNames.Add(escapedName);
							}

							parameterTypeModel = methodParameterTypeCache[param.Type] = new ParameterTypeModel()
							{
								Name = GetFullyQualifiedType(param.Type),
								EscapedName = escapedName,
								IsDisposable = param.Type.AllInterfaces.Any(type => type.Name == "IDisposable" && type.ContainingNamespace?.Name == "System"),
							};
						}

						return new ParameterModel()
						{
							Type = parameterTypeModel,
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
			Visibility = GetAccessibilityString(symbol.DeclaredAccessibility),
			Methods = new(methods),
		};
	}

	private static string GetAccessibilityString(Accessibility accessibility)
	{
		return accessibility switch
		{
			Accessibility.Private => "private",
			Accessibility.Public => "public",
			_ => "internal",
		};
	}

	private static SerializerModel? CreateSerializerModel(ClassModel classModel, MetadataBase metadata, bool isClient)
	{
		if (classModel.Methods.Length == 0)
		{
			return null;
		}

		var serializableTypes = metadata.UsesGenericSerialization
			? EquatableArray<ParameterTypeModel>.Empty
			: new(classModel.Methods.SelectMany(method => method.Parameters).Select(param => param.Type).Distinct());
		return new SerializerModel(metadata.UsesGenericSerialization, serializableTypes)
		{
			SupportsDeserialization = !isClient,
			SupportsSerialization = isClient,
			InterfaceNamespace = classModel.Namespace,
			InterfaceName = $"I{classModel.Name}Serializer",
			InterfaceVisiblity = classModel.Visibility,
		};
	}

	private abstract class MetadataBase
	{
		public required bool UsesGenericSerialization { get; init; }
	}
}
