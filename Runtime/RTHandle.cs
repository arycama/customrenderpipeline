using System.ComponentModel.Design.Serialization;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class RTHandle : IResourceHandle
{
    public int HandleIndex { get; }
    public bool IsPersistent { get; }
    public RtHandleDescriptor Descriptor { get; set; }

    public RTHandle(int handleIndex, bool isPersistent, RtHandleDescriptor descriptor)
    {
        HandleIndex = handleIndex;
        IsPersistent = isPersistent;
        Descriptor = descriptor;
    }
}