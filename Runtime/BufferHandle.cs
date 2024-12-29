using UnityEngine;

public class BufferHandle : IResourceHandle
{
    public int HandleIndex { get; }
    public bool IsPersistent { get; }
    public BufferHandleDescriptor Descriptor { get;  }

    public int Size => Descriptor.Stride * Descriptor.Count;
    
    public BufferHandle(int handleIndex, bool isPersistent, BufferHandleDescriptor descriptor)
    {
        HandleIndex = handleIndex;
        IsPersistent = isPersistent;
        Descriptor = descriptor; ;
    }
}