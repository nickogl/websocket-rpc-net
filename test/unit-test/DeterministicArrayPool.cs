using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Nickogl.WebSockets.Rpc.UnitTest;

/// <summary>
/// An array pool for testing that returns exactly the amount of requested bytes.
/// </summary>
/// <remarks>Additionally, this detects leaks and fails tests that produced them.</remarks>
internal sealed class DeterministicArrayPool<T> : ArrayPool<T>, IDisposable
{
	private readonly Dictionary<T[], string> _rented = [];

	public void Dispose()
	{
		if (_rented.Count > 0)
		{
			Assert.Fail($"One or more pooled arrays were leaked:\n- {string.Join("\n- ", _rented.Values)}");
		}
	}

	public override T[] Rent(int minimumLength)
	{
		var array = new T[minimumLength];
		_rented.Add(array, GetCallerInformation());
		return array;
	}

	public override void Return(T[] array, bool clearArray = false)
	{
		if (clearArray)
		{
			array.AsSpan().Clear();
		}

		if (!_rented.Remove(array))
		{
			throw new InvalidOperationException("Array was already returned or never rented in the first place");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetCallerInformation()
	{
		var stackFrame = new StackFrame(2, true);
		var method = stackFrame.GetMethod();
		var fileName = stackFrame.GetFileName();

		var result = new StringBuilder();
		if (fileName is not null)
		{
			result.Append(fileName);
			result.Append('(');
			result.Append(stackFrame.GetFileLineNumber());
			result.Append(',');
			result.Append(stackFrame.GetFileColumnNumber());
			result.Append("): ");
		}
		else
		{
			result.Append("<unknown filename>");
		}
		if (method is not null)
		{
			result.Append("at ");
			if (method.DeclaringType is not null)
			{
				result.Append(method.DeclaringType.FullName);
				result.Append('.');
			}
			result.Append(method.Name);
			result.Append("()");
		}
		else
		{
			result.Append("<unknown method>");
		}
		return result.ToString();
	}
}
