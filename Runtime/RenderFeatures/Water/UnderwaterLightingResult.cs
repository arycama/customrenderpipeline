using UnityEngine;
using UnityEngine.Rendering;

public readonly struct UnderwaterLightingResult : IRenderPassData
{
    private readonly ResourceHandle<RenderTexture> underwaterLighting;

    public UnderwaterLightingResult(ResourceHandle<RenderTexture> waterNormalFoam)
    {
        underwaterLighting = waterNormalFoam;
    }

	void IRenderPassData.SetInputs(RenderPass pass)
	{
        pass.ReadTexture("_UnderwaterResult", underwaterLighting);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
        pass.SetVector("_UnderwaterResultScaleLimit", pass.GetScaleLimit2D(underwaterLighting));
	}
}