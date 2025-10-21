using System;
using System.Collections;
using System.Collections.Generic;

public struct StructEnumeratorArray<T> : IStructEnumerator<T>
{
	private readonly T[] array;
	private int index;

	public StructEnumeratorArray(T[] array)
	{
		this.array = array ?? throw new ArgumentNullException(nameof(array));
		index = -1;
	}

	T IEnumerator<T>.Current => array[index];

	object IEnumerator.Current => array[index];

	void IDisposable.Dispose()
	{
	}

	bool IEnumerator.MoveNext() => ++index < array.Length;

	void IEnumerator.Reset() => index = -1;
}
