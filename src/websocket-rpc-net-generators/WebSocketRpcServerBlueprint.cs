namespace Nickogl.WebSockets.Rpc.Generators;

public readonly struct WebSocketRpcServerBlueprint(
	WebSocketRpcServerBlueprint.ServerMetadata metadata,
	string serverNamespace,
	string serverName,
	IReadOnlyCollection<WebSocketRpcServerBlueprint.Method> serverMethods,
	string clientNamespace,
	string clientName,
	IReadOnlyCollection<WebSocketRpcServerBlueprint.Method> clientMethods)
{
	public enum ParameterSerializationMode
	{
		Generic,
		Specialized,
	}

	public readonly struct ServerMetadata(ParameterSerializationMode serializationMode)
	{
		/// <summary>How to serialize RPC method parameters.</summary>
		public ParameterSerializationMode SerializationMode { get; } = serializationMode;
	}

	public enum MethodReturnType
	{
		Void,
		ValueTask,
		Task,
	}

	public readonly struct MethodMetadata(int key)
	{
		/// <summary>Unique, consistent key to identify method by.</summary>
		public int Key { get; } = key;
	}

	public readonly struct Method(
		string name,
		MethodReturnType returnType,
		IReadOnlyCollection<string> parameterTypes,
		MethodMetadata metadata)
	{
		/// <summary>Name of the RPC method.</summary>
		public string Name { get; } = name;

		/// <summary>Return type of the RPC method.</summary>
		public MethodReturnType ReturnType { get; } = returnType;

		/// <summary>Fully qualified parameter types of the RPC method.</summary>
		public IReadOnlyCollection<string> ParameterTypes { get; } = parameterTypes;

		/// <summary>Metadata to customize the source generation.</summary>
		public MethodMetadata Metadata { get; } = metadata;
	}

	/// <summary>Metadata to customize the source generation.</summary>
	public ServerMetadata Metadata { get; } = metadata;

	/// <summary>Namespace of the user-defined server interface.</summary>
	public string ServerNamespace { get; } = serverNamespace;

	/// <summary>Name of the user-defined server interface.</summary>
	public string ServerName { get; } = serverName;

	/// <summary>Available methods on the server.</summary>
	public IReadOnlyCollection<Method> ServerMethods { get; } = serverMethods;

	/// <summary>Namespace of the user-defined client interface.</summary>
	public string ClientNamespace { get; } = clientNamespace;

	/// <summary>Name of the user-defined client interface.</summary>
	public string ClientName { get; } = clientName;

	/// <summary>Available methods on the client.</summary>
	public IReadOnlyCollection<Method> ClientMethods { get; } = clientMethods;
}
