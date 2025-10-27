using UnityEngine;
using UnityEngine.Rendering;

public readonly struct VirtualTextureData : IRenderPassData
{
	private readonly Texture2DArray albedoSmoothness, normal, height;
	private readonly ResourceHandle<RenderTexture> indirection;
	private readonly float anisoLevel, virtualResolution;

	public VirtualTextureData(Texture2DArray albedoSmoothness, Texture2DArray normal, Texture2DArray height, ResourceHandle<RenderTexture> indirection, float anisoLevel, float virtualResolution)
	{
		this.albedoSmoothness = albedoSmoothness;
		this.normal = normal;
		this.height = height;
		this.indirection = indirection;
		this.anisoLevel = anisoLevel;
		this.virtualResolution = virtualResolution;
	}

	void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadTexture("IndirectionTexture", indirection);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetTexture("VirtualTexture", albedoSmoothness);
		pass.SetTexture("VirtualNormalTexture", normal);
		pass.SetTexture("VirtualHeightTexture", height);
		pass.SetFloat("AnisoLevel", anisoLevel);
		pass.SetFloat("VirtualUvScale", virtualResolution);
	}
}
