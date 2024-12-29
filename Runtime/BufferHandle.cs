public class BufferHandle : IResourceHandle
{
    public int Index { get; }
    public bool IsPersistent { get; }

    public BufferHandle(int index, bool isPersistent)
    {
        Index = index;
        IsPersistent = isPersistent;
    }
}