using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public readonly struct StructEnumerable<T> : IEnumerable<T>
{
	private readonly IEnumerable<T> enumerable;
	public int Count => enumerable.Count();

	public StructEnumerable(IEnumerable<T> enumerable) => this.enumerable = enumerable;

	IEnumerator<T> IEnumerable<T>.GetEnumerator()
	{
		switch (enumerable)
		{
			case List<T> list:
				return new StructEnumeratorList<T>(list);
			case T[] array:
				return new StructEnumeratorArray<T>(array);
			case HashSet<T> hashSet:
				return new StructEnumeratorHashSet<T>(hashSet);
			default:
				throw new InvalidOperationException(enumerable.GetType().ToString());
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		switch (enumerable)
		{
			case List<T> list:
				return new StructEnumeratorList<T>(list);
			case T[] array:
				return new StructEnumeratorArray<T>(array);
			case HashSet<T> hashSet:
				return new StructEnumeratorHashSet<T>(hashSet);
		}

		throw new InvalidOperationException(enumerable.GetType().ToString());
	}

	public static implicit operator StructEnumerable<T>(List<T> list) => new(list);
	public static implicit operator StructEnumerable<T>(T[] array) => new(array);
	public static implicit operator StructEnumerable<T>(HashSet<T> array) => new(array);
}
