public class RTHandle : IResourceHandle
{
    public int Index { get; }
    public bool IsPersistent { get; }

    public RTHandle(int index, bool isPersistent)
    {
        Index = index;
        IsPersistent = isPersistent;
    }
}