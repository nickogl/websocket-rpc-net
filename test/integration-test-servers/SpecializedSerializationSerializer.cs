using Nickogl.WebSockets.Rpc.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Nickogl.WebSockets.Rpc.IntegrationTest.Servers;

internal sealed class SpecializedSerializationSerializer
	: ISpecializedSerializationClientSerializer,
		ISpecializedSerializationServerSerializer,
		ISpecializedSerializationClientTestSerializer,
		ISpecializedSerializationServerTestSerializer
{
	private Utf8JsonWriter? _jsonWriter;

	public Event1 DeserializeEvent1(IRpcParameterReader reader)
	{
		return Deserialize(reader, SpecializedJsonSerializerContext.Default.Event1);
	}

	public Event2 DeserializeEvent2(IRpcParameterReader reader)
	{
		return Deserialize(reader, SpecializedJsonSerializerContext.Default.Event2);
	}

	public void SerializeEvent1(IRpcParameterWriter writer, Event1 parameter)
	{
		Serialize(writer, parameter, SpecializedJsonSerializerContext.Default.Event1);
	}

	public void SerializeEvent2(IRpcParameterWriter writer, Event2 parameter)
	{
		Serialize(writer, parameter, SpecializedJsonSerializerContext.Default.Event2);
	}

	private static T Deserialize<T>(IRpcParameterReader reader, JsonTypeInfo<T> jsonTypeInfo)
	{
		var jsonReader = new Utf8JsonReader(reader.Span);
		return JsonSerializer.Deserialize(ref jsonReader, jsonTypeInfo)
			?? throw new InvalidDataException($"Received null while deserializing object of type {typeof(T).Name}");
	}

	private void Serialize<T>(IRpcParameterWriter writer, T value, JsonTypeInfo<T> jsonTypeInfo)
	{
		_jsonWriter ??= new Utf8JsonWriter(writer);
		_jsonWriter.Reset(writer);
		JsonSerializer.Serialize(_jsonWriter, value, jsonTypeInfo);
		_jsonWriter.Flush();
		// The parameter writer continues to be referenced by the Utf8JsonWriter until
		// the next serialization, so will not be collected by the GC until then.
	}
}

[JsonSerializable(typeof(Event1))]
[JsonSerializable(typeof(Event2))]
internal partial class SpecializedJsonSerializerContext : JsonSerializerContext
{

}