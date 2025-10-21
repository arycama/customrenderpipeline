using System.Collections.Generic;

public static class ArrayPool<T>
{
	private static readonly Dictionary<int, Stack<T[]>> cache = new(new DefaultIntComparer());

	public static T[] Get(int length)
	{
		var pool = cache.GetOrAdd(length);
		return pool.PopOrCreate(length);
	}

	public static void Release(T[] array)
	{
		var pool = cache.GetOrAdd(array.Length);
		pool.Push(array);
	}

	public static ArrayScope<T> Get(int length, out T[] array)
	{
		array = Get(length);
		return new ArrayScope<T>(array);
	}
}

public readonly struct DefaultIntComparer : IEqualityComparer<int>
{
	public readonly bool Equals(int x, int y) => x == y;

	public readonly int GetHashCode(int obj) => obj;
}
