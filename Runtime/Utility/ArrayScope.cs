using System;

public readonly struct ArrayScope<T> : IDisposable
{
	private readonly T[] array;

	public ArrayScope(T[] array)
	{
		this.array = array;
	}

	readonly void IDisposable.Dispose()
	{
		ArrayPool<T>.Release(array);
	}
}