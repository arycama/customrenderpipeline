using System.Collections.Generic;

public static class StackExtensions
{
	public static T PopOrCreate<T>(this Stack<T> queue) where T : new()
	{
		if (!queue.TryPop(out var value))
			value = new T();

		return value;
	}

	public static T[] PopOrCreate<T>(this Stack<T[]> queue, int length)
	{
		if (!queue.TryPop(out var value))
			value = new T[length];

		return value;
	}
}