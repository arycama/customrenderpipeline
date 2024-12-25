using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class RTHandle
{
    public int Id { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public GraphicsFormat Format { get; set; }
    public bool EnableRandomWrite { get; set; }
    public int VolumeDepth { get; set; }
    public TextureDimension Dimension { get; set; }
    internal bool IsImported { get; set; }
    public bool IsNotReleasable { get; set; }

    public Vector3 Scale => new Vector3((float)Width / RenderTexture.width, (float)Height / RenderTexture.height, (float)VolumeDepth / RenderTexture.volumeDepth);
    public Vector3 Limit => new Vector3((Mathf.Floor(RenderTexture.width * Scale.x) - 0.5f) / RenderTexture.width, (Mathf.Floor(RenderTexture.height * Scale.y) - 0.5f) / RenderTexture.height, (Mathf.Floor(RenderTexture.volumeDepth * Scale.z) - 0.5f) / RenderTexture.volumeDepth);

    public Vector4 ScaleLimit2D => new Vector4(Scale.x, Scale.y, Limit.x, Limit.y);

    public bool IsScreenTexture { get; set; }
    public bool HasMips { get; set; }
    public bool AutoGenerateMips { get; set; }
    public int RenderTextureIndex { get; set; }

    // For persistent RT handles, they may get written to in some frames but not others, but we want to avoid re-allocating them
    // So set a flag to indicate they are already assigned
    public bool IsCreated { get; set; }

    public RenderTexture RenderTexture { get; set; }
    public int Index { get; }

    // Set for persistent RTs but cant be changed..
    public bool IsPersistent { get; }

    public RTHandle(int index, bool isPersistent)
    {
        Index = index;
        IsPersistent = isPersistent;
        IsNotReleasable = isPersistent;
    }

    public static implicit operator RenderTexture(RTHandle rtHandle)
    {
        return rtHandle.RenderTexture;
    }

    public static implicit operator RenderTargetIdentifier(RTHandle rtHandle)
    {
        return rtHandle.RenderTexture;
    }

    public override string ToString()
    {
        return $"RTHandle {Id} {Dimension} {Format} {Width}x{Height} ";
    }
}
