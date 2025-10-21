using System;
using System.Collections;
using System.Collections.Generic;

public struct StructEnumeratorList<T> : IStructEnumerator<T>
{
	private readonly List<T> list;
	private int index;

	public StructEnumeratorList(List<T> list)
	{
		this.list = list ?? throw new ArgumentNullException(nameof(list));
		index = -1;
	}

	T IEnumerator<T>.Current => list[index];

	object IEnumerator.Current => list[index];

	bool IEnumerator.MoveNext() => ++index < list.Count;

	void IEnumerator.Reset() => index = -1;

	void IDisposable.Dispose() { }
}
