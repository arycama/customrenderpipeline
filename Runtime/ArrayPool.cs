using System.Collections.Generic;

public static class ArrayPool<T>
{
    private static readonly Dictionary<int, Queue<T[]>> cache = new();

    public static T[] Get(int length) => !cache.TryGetValue(length, out var pool) || !pool.TryDequeue(out var result) ? new T[length] : result;

    public static void Release(T[] array)
    {
        if(!cache.TryGetValue(array.Length, out var pool))
        {
            pool = new Queue<T[]>();
            cache.Add(array.Length, pool);
        }

        pool.Enqueue(array);
    }
}