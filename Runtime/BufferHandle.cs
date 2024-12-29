public class BufferHandle : IResourceHandle<BufferHandleDescriptor>
{
    public int Index { get; }
    public bool IsPersistent { get; }
    public BufferHandleDescriptor Descriptor { get;  }

    public int Size => Descriptor.Stride * Descriptor.Count;
    
    public BufferHandle(int index, bool isPersistent, BufferHandleDescriptor descriptor)
    {
        Index = index;
        IsPersistent = isPersistent;
        Descriptor = descriptor; ;
    }
}