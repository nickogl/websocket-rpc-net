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

		return @$"while (__read < (__processed + {countExpression}))
			{{
				if (__read == __buffer.Length)
				{{
					int __newLength = __buffer.Length * 2;
					if (__newLength > _maximumMessageSize) throw new InvalidDataException(""Message exceeds maximum size"");
					var __newBuffer = _allocator.Rent(__newLength);
					__buffer.CopyTo(__newBuffer.AsSpan()); _allocator.Return(__buffer);
					__buffer = __newBuffer;
				}}
				var __destination = new Memory<byte>(__buffer, __read, __buffer.Length - __read);
				__result = await client.WebSocket.ReceiveAsync(__destination, __receiveCts.Token);
				if (__result.MessageType == WebSocketMessageType.Close) {{ try {{ await client.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }} catch {{ }} return; }}
				if (__result.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException($""Invalid message type: {{__result.MessageType}}"");
				__read += __result.Count;
				if (__result.EndOfMessage && __read < (__processed + {countExpression})) throw new InvalidDataException(""{tooFewBytesErrorMessage}"");
			}}
			{string.Format(assignmentFormat, $"__buffer.AsSpan(__processed, {countExpression})")};
			__processed += {countExpression};".Replace("\n\t\t\t", $"\n{Indent(nestingLevel)}");
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
				? $"Deserialize<{type}>({innerExpression})"
				: $"Deserialize{GetEscapedParameterType(type)}({innerExpression})";
	}

	private static string GenerateSerializeCall(string type, SerializerModel? serializerModel, string innerExpression)
	{
		return serializerModel == null
			? innerExpression
			: serializerModel.Value.IsGeneric
				? $"Serialize<{type}>({innerExpression})"
				: $"Serialize{GetEscapedParameterType(type)}({innerExpression})";
	}

	internal static string GenerateParameterList(IEnumerable<ParameterModel> parameters, bool types = true)
	{
		return types
			? string.Join(", ", parameters.Select(param => $"{param.Type} {param.Name}"))
			: string.Join(", ", parameters.Select(param => param.Name));
	}

	private static string GenerateArgumentMatcherList(IEnumerable<ParameterModel> parameters)
	{
		return string.Join(", ", parameters.Select(param => $"RpcArgMatcher<{param.Type}> {param.Name}"));
	}
}
