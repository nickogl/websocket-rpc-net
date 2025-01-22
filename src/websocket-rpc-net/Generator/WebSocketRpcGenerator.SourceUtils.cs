using Nickogl.WebSockets.Rpc.Models;

namespace Nickogl.WebSockets.Rpc.Generator;

public partial class WebSocketRpcGenerator
{
	private static string Indent(int nestingLevel)
	{
		return new string('\t', nestingLevel);
	}

	// TBD: Read the whole message at once?
	private static string GenerateReadExactly(string countExpression, string assignmentFormat, string tooFewBytesErrorMessage, int nestingLevel)
	{
		if (nestingLevel < 1)
		{
			throw new ArgumentException("Nesting level must be greater than or equal to 1");
		}

		return @$"while (read < (processed + {countExpression}))
			{{
				if (read == buffer.Length)
				{{
					int newLength = buffer.Length * 2;
					if (newLength > _maximumMessageSize) throw new InvalidDataException(""Message exceeds maximum size"");
					var newBuffer = _allocator.Rent(newLength);
					buffer.CopyTo(newBuffer.AsSpan()); _allocator.Return(buffer);
					buffer = newBuffer;
				}}
				var destination = new Memory<byte>(buffer, read, buffer.Length - read);
				result = await client.WebSocket.ReceiveAsync(destination, cancellationToken);
				if (result.MessageType == WebSocketMessageType.Close) return;
				else if (result.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException($""Invalid message type: {{result.MessageType}}"");
				read += result.Count;
				if (result.EndOfMessage && read < (processed + {countExpression})) throw new InvalidDataException(""{tooFewBytesErrorMessage}"");
			}}
			{string.Format(assignmentFormat, $"buffer.AsSpan(processed, {countExpression})")};
			processed += {countExpression};".Replace("\n\t\t\t", $"\n{Indent(nestingLevel)}");
	}

	private static string GenerateReadInt32(string intoVariable, string tooFewBytesErrorMessage, int nestingLevel)
	{
		return GenerateReadExactly("sizeof(int)", $"int {intoVariable} = BinaryPrimitives.ReadInt32LittleEndian({{0}})", tooFewBytesErrorMessage, nestingLevel);
	}

	private static string GenerateDeserializeCall(string type, SerializerModel? serializerModel, string innerExpression)
	{
		return serializerModel == null
			? innerExpression
			: serializerModel.Value.IsGeneric
				? $"_serializer.Deserialize<{type}>({innerExpression})"
				: $"_serializer.Deserialize{GetEscapedParameterType(type)}({innerExpression})";
	}

	private static string GenerateSerializeCall(string type, SerializerModel? serializerModel, string innerExpression)
	{
		return serializerModel == null
			? innerExpression
			: serializerModel.Value.IsGeneric
				? $"_serializer.Serialize<{type}>({innerExpression})"
				: $"_serializer.Serialize{GetEscapedParameterType(type)}({innerExpression})";
	}
}
