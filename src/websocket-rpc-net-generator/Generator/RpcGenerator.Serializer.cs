using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class RpcServerGenerator
{
	private static void GenerateSerializerInterface(SourceProductionContext context, SerializerModel serializerModel)
	{
		var serializerInterface = new StringBuilder(@$"
using Nickogl.WebSockets.Rpc.Serialization;

namespace {serializerModel.InterfaceNamespace};

/// <summary>
/// Serialize or deserialize RPC parameters for a websocket-rpc client or server.
/// You need to implement this interface and provide an instance of that class
/// to the generated code by implementing the corresponding partial method.
/// </summary>
{serializerModel.InterfaceVisiblity} interface {serializerModel.InterfaceName}
{{");
		if (serializerModel.IsGeneric)
		{
			if (serializerModel.SupportsDeserialization)
			{
				serializerInterface.AppendLine(@$"
	/// <summary>
	/// Deserialize an RPC parameter of type <typeparamref name=""T""/>.
	/// </summary>
	/// <param name=""reader"">Reader to read parameter data from the raw bytes or the <see cref=""System.IO.Stream""/> API.</param>
	T Deserialize<T>(IRpcParameterReader reader);");
			}
			if (serializerModel.SupportsSerialization)
			{
				serializerInterface.AppendLine(@$"
	/// <summary>
	/// Serialize an RPC parameter of type <typeparamref name=""T""/>.
	/// </summary>
	/// <param name=""writer"">Writer to write parameter data using either the <see cref=""System.Buffers.IBufferWriter""/> or the <see cref=""System.IO.Stream""/> API.</param>
	/// <param name=""parameter"">Parameter to serialize using the provided <paramref name=""writer""/>.</param>
	void Serialize<T>(IRpcParameterWriter writer, T parameter);");
			}
		}
		else
		{
			foreach (var type in serializerModel.Types)
			{
				var xmlEscapedType = type.Name.Replace("<", "{").Replace(">", "}");
				if (serializerModel.SupportsDeserialization)
				{
					serializerInterface.AppendLine(@$"
	/// <summary>
	/// Deserialize an RPC parameter of type <see cref=""{xmlEscapedType}""/>.
	/// </summary>
	/// <param name=""reader"">Reader to read parameter data from the raw bytes or the <see cref=""System.IO.Stream""/> API.</param>
	{type.Name} Deserialize{type.EscapedName}(IRpcParameterReader reader);");
				}
				if (serializerModel.SupportsSerialization)
				{
					serializerInterface.AppendLine(@$"
	/// <summary>
	/// Serialize an RPC parameter of type <see cref=""{xmlEscapedType}""/>.
	/// </summary>
	/// <param name=""writer"">Writer to write parameter data using either the <see cref=""System.Buffers.IBufferWriter""/> or the <see cref=""System.IO.Stream""/> API.</param>
	/// <param name=""parameter"">Parameter to serialize using the provided <paramref name=""writer""/>.</param>
	void Serialize{type.EscapedName}(IRpcParameterWriter writer, {type.Name} parameter);");
				}
			}
		}
		serializerInterface.AppendLine("}");
		context.AddSource($"{serializerModel.InterfaceName}.g.cs", serializerInterface.ToString());
	}
}
