using System.Collections;

namespace Nickogl.WebSockets.Rpc;

/// <summary>
/// An immutable, equatable array that implements structural equality comparison.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T>
{
	public static readonly EquatableArray<T> Empty = new([]);

	private readonly T[] _array;

	public int Length => _array.Length;

	public EquatableArray()
	{
		_array = [];
	}

	public EquatableArray(T[] array)
	{
		_array = array;
	}

	public EquatableArray(IEnumerable<T> values)
	{
		_array = [.. values];
	}

	public bool Equals(EquatableArray<T> array)
	{
		return AsSpan().SequenceEqual(array.AsSpan());
	}

	public override bool Equals(object? obj)
	{
		return obj is EquatableArray<T> array && Equals(array);
	}

	public override int GetHashCode()
	{
		var hashCode = new HashCode();
		foreach (T item in _array)
		{
			hashCode.Add(item);
		}
		return hashCode.ToHashCode();
	}

	public ReadOnlySpan<T> AsSpan()
	{
		return _array.AsSpan();
	}

	public IEnumerator<T> GetEnumerator()
	{
		return _array.AsEnumerable().GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
	{
		return !left.Equals(right);
	}
}
