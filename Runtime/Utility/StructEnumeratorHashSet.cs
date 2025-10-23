using System;
using System.Collections;
using System.Collections.Generic;

public readonly struct StructEnumeratorHashSet<T> : IStructEnumerator<T>
{
	private readonly IEnumerator<T> enumerator;

	public StructEnumeratorHashSet(HashSet<T> hashSet)
	{
		if (hashSet == null)
			throw new ArgumentNullException(nameof(hashSet));

		enumerator = hashSet.GetEnumerator();
	}

	T IEnumerator<T>.Current => enumerator.Current;

	object IEnumerator.Current => enumerator.Current;

	void IDisposable.Dispose() => enumerator.Dispose();

	bool IEnumerator.MoveNext() => enumerator.MoveNext();

	void IEnumerator.Reset() => enumerator.Reset();
}
