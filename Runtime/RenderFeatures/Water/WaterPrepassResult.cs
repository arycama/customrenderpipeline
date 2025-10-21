using UnityEngine;
using UnityEngine.Rendering;

public readonly struct WaterPrepassResult : IRenderPassData
{
    private readonly ResourceHandle<RenderTexture> waterNormalFoam, waterTriangleNormal;
    private readonly Float3 albedo, extinction;

    public WaterPrepassResult(ResourceHandle<RenderTexture> waterNormalFoam, ResourceHandle<RenderTexture> waterTriangleNormal, Float3 albedo, Float3 extinction)
    {
        this.waterNormalFoam = waterNormalFoam;
        this.waterTriangleNormal = waterTriangleNormal;
        this.albedo = albedo;
        this.extinction = extinction;
    }

	readonly void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadTexture("_WaterNormalFoam", waterNormalFoam);
		pass.ReadTexture("_WaterTriangleNormal", waterTriangleNormal);
	}

	readonly void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetVector("_WaterAlbedo", albedo);
		pass.SetVector("_WaterExtinction", extinction);
	}
}