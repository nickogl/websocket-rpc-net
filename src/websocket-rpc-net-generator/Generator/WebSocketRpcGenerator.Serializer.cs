using Microsoft.CodeAnalysis;
using Nickogl.WebSockets.Rpc.Models;
using System.Text;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	private static void GenerateSerializerInterface(SourceProductionContext context, SerializerModel serializerModel)
	{
		var serializerInterface = new StringBuilder(@$"
using Nickogl.WebSockets.Rpc;

namespace {serializerModel.InterfaceNamespace};

/// <summary>
/// Serialize or deserialize RPC parameters for a websocket-rpc client or server.
/// You need to implement this interface and provide an instance of that class
/// to the generated code by implementing the corresponding partial method.
/// </summary>
internal interface {serializerModel.InterfaceName}
{{");
		if (serializerModel.IsGeneric)
		{
			if (serializerModel.SupportsDeserialization)
			{
				serializerInterface.AppendLine(@$"
	/// <summary>
	/// Deserialize an RPC parameter of type <typeparamref name=""T""/> from <paramref name=""data""/>.
	/// </summary>
	/// <param name=""reader"">Reader to read parameter data using either the <see cref=""System.IO.Stream""/> or the <see cref=""System.ReadOnlySpan{{byte}}""/> API.</param>
	T Deserialize<T>(IParameterReader reader);");
			}
			if (serializerModel.SupportsSerialization)
			{
				serializerInterface.AppendLine(@$"
	/// <summary>
	/// Serialize an RPC parameter of type <typeparamref name=""T""/>.
	/// </summary>
	/// <param name=""writer"">Writer to write parameter data using either the <see cref=""System.IO.Stream""/> or the <see cref=""System.Buffers.IBufferWriter""/> API.</param>
	/// <param name=""parameter"">Parameter to serialize to raw data using the <paramref name=""writer""/>.</param>
	void Serialize<T>(IParameterWriter writer, T parameter);");
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
	/// <param name=""reader"">Reader to read parameter data using either the <see cref=""System.IO.Stream""/> or the <see cref=""System.ReadOnlySpan{{byte}}""/> API.</param>
	{type.Name} Deserialize{type.EscapedName}(IParameterReader reader);");
				}
				if (serializerModel.SupportsSerialization)
				{
					serializerInterface.AppendLine(@$"
	/// <summary>
	/// Serialize an RPC parameter of type <see cref=""{xmlEscapedType}""/>.
	/// </summary>
	/// <param name=""writer"">Writer to write parameter data using either the <see cref=""System.IO.Stream""/> or the <see cref=""System.Buffers.IBufferWriter""/> API.</param>
	/// <param name=""parameter"">Parameter to serialize to raw data using the <paramref name=""writer""/>.</param>
	void Serialize{type.EscapedName}(IParameterWriter writer, {type.Name} parameter);");
				}
			}
		}
		serializerInterface.AppendLine("}");
		context.AddSource($"{serializerModel.InterfaceName}.g.cs", serializerInterface.ToString());
	}
}
