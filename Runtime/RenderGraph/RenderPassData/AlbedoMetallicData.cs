using UnityEngine;

public class AlbedoMetallicData : RTHandleData
{
	public AlbedoMetallicData(ResourceHandle<RenderTexture> handle) : base(handle, "GbufferAlbedoMetallic")
	{
	}
}
