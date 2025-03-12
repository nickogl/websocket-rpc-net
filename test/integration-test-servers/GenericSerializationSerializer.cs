using Nickogl.WebSockets.Rpc.Serialization;
using System.Text.Json;

namespace Nickogl.WebSockets.Rpc.IntegrationTest.Servers;

internal sealed class GenericSerializationSerializer
	: IGenericSerializationClientSerializer,
		IGenericSerializationServerSerializer,
		IGenericSerializationClientTestSerializer,
		IGenericSerializationServerTestSerializer
{
	private Utf8JsonWriter? _jsonWriter;

	public T Deserialize<T>(IRpcParameterReader reader)
	{
		var jsonReader = new Utf8JsonReader(reader.Span);
		return JsonSerializer.Deserialize<T>(ref jsonReader)
			?? throw new InvalidDataException($"Received null while deserializing object of type {typeof(T).Name}");
	}

	public void Serialize<T>(IRpcParameterWriter writer, T parameter)
	{
		_jsonWriter ??= new Utf8JsonWriter(writer);
		_jsonWriter.Reset(writer);
		JsonSerializer.Serialize(_jsonWriter, parameter);
		_jsonWriter.Flush();
		// The parameter writer continues to be referenced by the Utf8JsonWriter until
		// the next serialization, so will not be collected by the GC until then.
	}
}