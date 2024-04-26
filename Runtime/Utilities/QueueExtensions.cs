using System;
using System.Collections.Generic;

public static class QueueExtensions
{
    public static T DequeueOrCreate<T>(this Queue<T> queue) where T : new()
    {
        if (!queue.TryDequeue(out var value))
            value = new T();

        return value;
    }

    public static T[] DequeueOrCreate<T>(this Queue<T[]> queue, int length)
    {
        if (!queue.TryDequeue(out var value))
            value = new T[length];

        return value;
    }
}