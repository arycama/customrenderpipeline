using System;
using System.Collections.Generic;

public static class DictionaryExtensions
{
	public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, Func<TValue> createAction)
	{
		if (!dictionary.TryGetValue(key, out var value))
		{
			value = createAction();
			dictionary.Add(key, value);
		}

		return value;
	}

	public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key) where TValue : new()
	{
		return GetOrAdd(dictionary, key, () => new TValue());
	}
}