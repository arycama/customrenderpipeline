public class RTHandle : IResourceHandle<RtHandleDescriptor>
{
    public int Index { get; }
    public bool IsPersistent { get; }
    public RtHandleDescriptor Descriptor { get; }

    public RTHandle(int index, bool isPersistent, RtHandleDescriptor descriptor)
    {
        Index = index;
        IsPersistent = isPersistent;
        Descriptor = descriptor;
    }
}