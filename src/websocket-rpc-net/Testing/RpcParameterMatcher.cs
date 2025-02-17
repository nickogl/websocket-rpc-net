using System.Collections;

namespace Nickogl.WebSockets.Rpc.Testing;

/// <summary>
/// Match a parameter in a remote procedure call received by a test client.
/// </summary>
/// <param name="predicate">Predicate to match parameters of type <typeparamref name="T"/>.</param>
/// <typeparam name="T">Type of the argument to match.</typeparam>
public readonly struct RpcParameterMatcher<T>(Func<T, bool> predicate)
{
	private readonly Func<T, bool> _predicate = predicate;

	/// <summary>
	/// Check if the provided RPC parameter matches.
	/// </summary>
	/// <param name="parameter">RPC parameter to check.</param>
	/// <returns>True if the parameter matches.</returns>
	public bool Matches(T parameter)
	{
		return _predicate(parameter);
	}

	/// <summary>
	/// Match an RPC parameter by its exact value.
	/// </summary>
	/// <param name="value">Value to match.</param>
	public static implicit operator RpcParameterMatcher<T>(T value)
	{
		return new RpcParameterMatcher<T>(parameter =>
		{
			if (parameter is null && value is null)
			{
				return true;
			}
			if (parameter is null || value is null)
			{
				return false;
			}
			if (parameter is IEnumerable argList && value is IEnumerable valueList)
			{
				object[] parameterArray = [.. argList];
				object[] valueArray = [.. valueList];
				return parameterArray.SequenceEqual(valueArray);
			}
			return parameter.Equals(value);
		});
	}
}
