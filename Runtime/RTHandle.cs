using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class RTHandle : ResourceHandle<RenderTexture>
{
    public Vector3 Scale => new Vector3((float)Width / Resource.width, (float)Height / Resource.height, (float)VolumeDepth / Resource.volumeDepth);
    public Vector3 Limit => new Vector3((Mathf.Floor(Resource.width * Scale.x) - 0.5f) / Resource.width, (Mathf.Floor(Resource.height * Scale.y) - 0.5f) / Resource.height, (Mathf.Floor(Resource.volumeDepth * Scale.z) - 0.5f) / Resource.volumeDepth);
    public Vector4 ScaleLimit2D => new Vector4(Scale.x, Scale.y, Limit.x, Limit.y);

    public int Width { get; set; }
    public int Height { get; set; }
    public GraphicsFormat Format { get; set; }
    public bool EnableRandomWrite { get; set; }
    public int VolumeDepth { get; set; }
    public TextureDimension Dimension { get; set; }
    public bool IsScreenTexture { get; set; }
    public bool HasMips { get; set; }
    public bool AutoGenerateMips { get; set; }

    public RTHandle(int handleIndex, bool isImported, bool isPersistent) : base(handleIndex, isImported, isPersistent)
    {
    }

    public static implicit operator RenderTexture(RTHandle rtHandle)
    {
        return rtHandle.Resource;
    }

    public static implicit operator RenderTargetIdentifier(RTHandle rtHandle)
    {
        return rtHandle.Resource;
    }

    public override string ToString()
    {
        return $"{Dimension} {Format} {Width}x{Height} ";
    }
}
