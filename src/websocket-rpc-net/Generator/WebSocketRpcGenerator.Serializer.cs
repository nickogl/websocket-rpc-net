using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	private static void GenerateSerializerInterface(SourceProductionContext context, SerializerModel serializerModel)
	{
		var serializerInterface = new StringBuilder(@$"
namespace {serializerModel.InterfaceNamespace};

/// <summary>
/// Serialize or deserialize RPC parameters for a websocket-rpc client or server.
/// You need to implement this interface and provide an instance of that class
/// to the generated code by implementing the corresponding partial method.
/// </summary>
/// <remarks>
/// If the client and server share parameter types and you want them to have the
/// same wire format, you can implement both the client and server serializer
/// interface within the same class. This avoids the need to duplicate code.
/// </remarks>
public interface {serializerModel.InterfaceName}
{{");
		if (serializerModel.IsGeneric)
		{
			if (serializerModel.SupportsDeserialization)
			{
				serializerInterface.AppendLine(@$"
	/// <summary>
	/// Deserialize an RPC method parameter of type <typeparamref name=""T""/> from <paramref name=""data""/>.
	/// </summary>
	T Deserialize<T>(ReadOnlySpan<byte> data);");
			}
			if (serializerModel.SupportsSerialization)
			{
				serializerInterface.AppendLine(@$"
	/// <summary>
	/// Serialize an RPC method parameter of type <typeparamref name=""T""/>.
	/// </summary>
	ReadOnlySpan<byte> Serialize<T>(T data);");
			}
		}
		else
		{
			foreach (var type in serializerModel.Types)
			{
				var escapedType = GetEscapedParameterType(type);
				var xmlEscapedType = type.Replace("<", "&lt;").Replace(">", "&gt;");
				if (serializerModel.SupportsDeserialization)
				{
					serializerInterface.AppendLine(@$"
	/// <summary>
	/// Deserialize an RPC method parameter of type <c>{xmlEscapedType}</c> from <paramref name=""data""/>.
	/// </summary>
	{type} Deserialize{escapedType}(ReadOnlySpan<byte> data);");
				}
				if (serializerModel.SupportsSerialization)
				{
					serializerInterface.AppendLine(@$"
	/// <summary>
	/// Serialize an RPC method parameter of type <c>{xmlEscapedType}</c>.
	/// </summary>
	ReadOnlySpan<byte> Serialize{escapedType}({type} data);");
				}
			}
		}
		serializerInterface.AppendLine("}");
		context.AddSource($"{serializerModel.InterfaceName}.g.cs", serializerInterface.ToString());
	}
}
