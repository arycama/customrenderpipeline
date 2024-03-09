using System;
using System.Collections.Generic;

public static class QueueExtensions
{
    public static T DequeueOrCreate<T>(this Queue<T> queue, Func<T> createAction)
    {
        if (!queue.TryDequeue(out var value))
            value = createAction();

        return value;
    }

    public static T DequeueOrCreate<T>(this Queue<T> queue) where T : new()
    {
       return DequeueOrCreate(queue, () => new T());
    }
}