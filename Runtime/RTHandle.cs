using UnityEngine;
using UnityEngine.Assertions;
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
    public bool IsImported { get; set; }
    public Vector3 Scale => new Vector3((float)Width / RenderTexture.width, (float)Height / RenderTexture.height, (float)VolumeDepth / RenderTexture.volumeDepth);
    public Vector3 Limit => new Vector3((Mathf.Floor(RenderTexture.width * Scale.x) - 0.5f) / RenderTexture.width, (Mathf.Floor(RenderTexture.height * Scale.y) - 0.5f) / RenderTexture.height, (Mathf.Floor(RenderTexture.depth * Scale.z) - 0.5f) / RenderTexture.volumeDepth);

    public bool IsScreenTexture { get; set; }
    public bool HasMips { get; set; }

    public RenderTexture RenderTexture { get; set; }

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
