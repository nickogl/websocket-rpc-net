using System.Text;

namespace Nickogl.WebSockets.Rpc.Generators;

public readonly struct WebSocketRpcServerBlueprint(
	WebSocketRpcServerBlueprint.ServerMetadata metadata,
	string serverNamespace,
	string serverName,
	IReadOnlyCollection<WebSocketRpcServerBlueprint.Method> serverMethods,
	string clientNamespace,
	string clientName,
	IReadOnlyCollection<WebSocketRpcServerBlueprint.Method> clientMethods,
	IReadOnlyCollection<WebSocketRpcServerBlueprint.Property> clientProperties,
	IReadOnlyCollection<string> clientNonRpcMethods)
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

	public readonly struct Parameter(string type, string name, string escapedType)
	{
		/// <summary>Fully qualified type name of the parameter.</summary>
		public string Type { get; } = type;

		/// <summary>Name of the parameter.</summary>
		public string Name { get; } = name;

		/// <summary>Escaped version of <see cref="Type"/> for use in e.g. method names.</summary>
		public string EscapedType { get; } = escapedType;
	}

	public readonly struct Method(
		string name,
		MethodReturnType returnType,
		IReadOnlyCollection<Parameter> parameters,
		MethodMetadata metadata)
	{
		/// <summary>Name of the RPC method.</summary>
		public string Name { get; } = name;

		/// <summary>Return type of the RPC method.</summary>
		public MethodReturnType ReturnType { get; } = returnType;

		/// <summary>Parameters of the RPC method.</summary>
		public IReadOnlyCollection<Parameter> Parameters { get; } = parameters;

		/// <summary>Metadata to customize the source generation.</summary>
		public MethodMetadata Metadata { get; } = metadata;
	}

	public readonly struct Property(string type, string name, bool isReadOnly)
	{
		/// <summary>Fully qualified type name of the generated property.</summary>
		public string Type { get; } = type;

		/// <summary>Name of the generated property.</summary>
		public string Name { get; } = name;

		/// <summary>Whether or not the generated property is read-only.</summary>
		public bool IsReadOnly { get; } = isReadOnly;
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

	/// <summary>Available state on the client.</summary>
	public IReadOnlyCollection<Property> ClientProperties { get; } = clientProperties;

	/// <summary>Available non-rpc methods on the client. Generated as-is.</summary>
	public IReadOnlyCollection<string> ClientNonRpcMethods { get; } = clientNonRpcMethods;
}
