public readonly struct ResourceHandle<T>
{
	public readonly int index;

	public ResourceHandle(int index)
	{
		this.index = index;
	}

	public override int GetHashCode()
	{
		return index;
	}
}