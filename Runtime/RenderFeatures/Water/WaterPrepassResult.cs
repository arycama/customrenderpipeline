using UnityEngine;
using UnityEngine.Rendering;

public struct WaterPrepassResult : IRenderPassData
{
    private readonly ResourceHandle<RenderTexture> waterNormalFoam, waterTriangleNormal;
    private Vector3 albedo, extinction;

    public WaterPrepassResult(ResourceHandle<RenderTexture> waterNormalFoam, ResourceHandle<RenderTexture> waterTriangleNormal, Vector3 albedo, Vector3 extinction)
    {
        this.waterNormalFoam = waterNormalFoam;
        this.waterTriangleNormal = waterTriangleNormal;
        this.albedo = albedo;
        this.extinction = extinction;
    }

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
		pass.ReadTexture("_WaterNormalFoam", waterNormalFoam);
		pass.ReadTexture("_WaterTriangleNormal", waterTriangleNormal);
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
		pass.SetVector("_WaterAlbedo", albedo);
		pass.SetVector("_WaterExtinction", extinction);
	}
}