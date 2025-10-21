using System;

public readonly struct StructEnumerator<T>
{
	private readonly int count;
	private readonly Func<int, T> selector;

	public StructEnumerator(int count, Func<int, T> selector)
	{
		this.count = count;
		this.selector = selector;
	}

	public Enumerator GetEnumerator() => new(count, selector);

	public struct Enumerator
	{
		private readonly int _count;
		private readonly Func<int, T> _selector;
		private int _index;

		public Enumerator(int count, Func<int, T> selector)
		{
			_count = count;
			_selector = selector;
			_index = -1;
		}

		public T Current => _selector(_index);
		public bool MoveNext() => ++_index < _count;
	}
}