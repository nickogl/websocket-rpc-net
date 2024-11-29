using Nickogl.WebSockets.Rpc.Attributes;

namespace Nickogl.WebSockets.Rpc.Generators;

public readonly struct WebSocketRpcServerBlueprint(
	WebSocketRpcServerAttribute parameters,
	string serverNamespace,
	string serverName,
	IReadOnlyCollection<WebSocketRpcServerBlueprint.Method> serverMethods,
	string clientNamespace,
	string clientName,
	IReadOnlyCollection<WebSocketRpcServerBlueprint.Method> clientMethods)
{
	public readonly struct Method(
		string returnType,
		string name,
		IReadOnlyCollection<string> parameterTypes,
		WebSocketRpcMethodAttribute metadata)
	{
		/// <summary>Fully qualified return type of the RPC method.</summary>
		public string ReturnType { get; } = returnType;

		/// <summary>Name of the RPC method.</summary>
		public string Name { get; } = name;

		/// <summary>Fully qualified parameter types of the RPC method.</summary>
		public IReadOnlyCollection<string> ParameterTypes { get; } = parameterTypes;

		/// <summary>Metadata to customize the source generation.</summary>
		public WebSocketRpcMethodAttribute Metadata { get; } = metadata;
	}

	/// <summary>Metadata to customize the source generation.</summary>
	public WebSocketRpcServerAttribute Metadata { get; } = parameters;

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
