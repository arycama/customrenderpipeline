using System.Collections.Generic;

public static class ArrayPool<T>
{
    private static readonly Dictionary<int, Queue<T[]>> cache = new();

    public static T[] Get(int length)
    {
        var pool = cache.GetOrAdd(length);
        return pool.DequeueOrCreate(length);
    }

    public static void Release(T[] array)
    {
        var pool = cache.GetOrAdd(array.Length);
        pool.Enqueue(array);
    }
}