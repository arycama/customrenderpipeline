public readonly struct ResourceHandle<T>
{
    public int Index { get; }
    public bool IsPersistent { get; }

    public ResourceHandle(int index, bool isPersistent)
    {
        Index = index;
        IsPersistent = isPersistent;
    }

    public override string ToString() => $"{Index} {IsPersistent}";
}