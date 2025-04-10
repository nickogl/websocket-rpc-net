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

	private static void GenerateJsonSerializer(SourceProductionContext context, SerializerModel serializerModel)
	{
		var serializerContextClass = new StringBuilder(@$"
using System.Text.Json.Serialization;

namespace {serializerModel.InterfaceNamespace};
");
		foreach (var type in serializerModel.Types)
		{
			serializerContextClass.Append(@$"
[JsonSerializable(typeof({type.Name}), TypeInfoPropertyName = ""{type.EscapedName}"")]");
		}
		serializerContextClass.Append(@$"
{serializerModel.InterfaceVisiblity} partial class {serializerModel.InterfaceName}Context : JsonSerializerContext
{{
}}");
		context.AddSource($"{serializerModel.InterfaceName}Context.g.cs", serializerContextClass.ToString());

		var serializerClass = new StringBuilder(@$"
using Nickogl.WebSockets.Rpc.Serialization;
using System.Text.Json;

namespace {serializerModel.InterfaceNamespace};

{serializerModel.InterfaceVisiblity} readonly struct {serializerModel.InterfaceName}
{{");
		if (serializerModel.SupportsSerialization)
		{
			serializerClass.AppendLine(@$"
	private readonly Utf8JsonWriter _jsonWriter;

	public {serializerModel.InterfaceName}()
	{{
		_jsonWriter = new Utf8JsonWriter(Stream.Null);
	}}");
		}

		foreach (var type in serializerModel.Types)
		{
			if (serializerModel.SupportsDeserialization)
			{
				serializerClass.Append(@$"
	{type.Name} Deserialize{type.EscapedName}(IRpcParameterReader reader)
	{{
		var jsonReader = new Utf8JsonReader(reader.Span);
		var result = JsonSerializer.Deserialize(ref jsonReader, {serializerModel.InterfaceName}Context.Default.{type.EscapedName});");
				if (type.Name[type.Name.Length - 1] != '?')
				{
					serializerClass.Append(@$"
		if (result is null)
		{{
			throw new InvalidDataException(""Received null while deserializing object of type '{type.Name}'"");
		}}");
				}
				serializerClass.AppendLine(@$"
		return result;
	}}");
			}
			if (serializerModel.SupportsSerialization)
			{
			}
		}

		serializerClass.AppendLine("}");
		context.AddSource($"{serializerModel.InterfaceName}.g.cs", serializerClass.ToString());
	}
}
