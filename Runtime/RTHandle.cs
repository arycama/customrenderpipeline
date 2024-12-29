using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class RTHandle : IResourceHandle
{
    public int HandleIndex { get; }
    public bool IsPersistent { get; }
    public int Width { get; set; }
    public int Height { get; set; }
    public GraphicsFormat Format { get; set; }
    public bool EnableRandomWrite { get; set; }
    public int VolumeDepth { get; set; }
    public TextureDimension Dimension { get; set; }
    public bool IsScreenTexture { get; set; }
    public bool HasMips { get; set; }
    public bool AutoGenerateMips { get; set; }

    public RTHandle(int handleIndex, bool isPersistent)
    {
        HandleIndex = handleIndex;
        IsPersistent = isPersistent;
    }
}