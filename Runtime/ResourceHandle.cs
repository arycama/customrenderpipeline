public readonly struct ResourceHandle<T>
{
    public int Index { get; }

    public ResourceHandle(int index)
    {
        Index = index;
    }
}