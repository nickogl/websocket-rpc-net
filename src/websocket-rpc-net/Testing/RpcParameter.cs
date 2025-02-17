namespace Nickogl.WebSockets.Rpc.Testing;

/// <summary>
/// Helpers to create instances of <see cref="RpcParameterMatcher{T}"/>.
/// </summary>
/// <remarks>
/// <para>Used to verify or await specific remote procedure calls.</para>
/// <para>Examples:
/// <list type="bullet">
/// <item><c>await testClient.Received.Foo("ExactValue")</c></item>
/// <item><c>await testClient.Received.Foo(RpcParameter.Is&lt;string&gt;(arg => arg.StartsWith("MatchesValue")))</c></item>
/// <item><c>await testClient.Received.Foo(RpcParameter.Any&lt;string&gt;())</c></item>
/// </list>
/// </para>
/// </remarks>
public static class RpcParameter
{
	/// <summary>
	/// Create a matcher that only intercepts RPC calls if the provided predicate succeeds.
	/// </summary>
	/// <param name="predicate">Predicate to check the transmitted parameter.</param>
	/// <typeparam name="T">Type of the parameter to check.</typeparam>
	/// <returns>A parameter matcher used when verifying or awaiting RPC messages.</returns>
	public static RpcParameterMatcher<T> Is<T>(Func<T, bool> predicate)
	{
		return new RpcParameterMatcher<T>(predicate);
	}

	/// <summary>
	/// Create a matcher that always intercepts RPC calls for this parameter.
	/// </summary>
	/// <typeparam name="T">Type of the parameter to check.</typeparam>
	/// <returns>A parameter matcher used when verifying or awaiting RPC messages.</returns>
	public static RpcParameterMatcher<T> Any<T>()
	{
		return new RpcParameterMatcher<T>(_ => true);
	}
}
