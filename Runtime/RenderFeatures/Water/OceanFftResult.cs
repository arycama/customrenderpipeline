using UnityEngine;
using UnityEngine.Rendering;

public readonly struct OceanFftResult : IRenderPassData
{
    private readonly ResourceHandle<RenderTexture> oceanDisplacement;
    private readonly ResourceHandle<RenderTexture> oceanDisplacementHistory;
    private readonly ResourceHandle<RenderTexture> oceanNormalFoamSmoothness;
    private readonly ResourceHandle<RenderTexture> lengthToRoughness;
    private readonly ResourceHandle<GraphicsBuffer> oceanBuffer;

    public OceanFftResult(ResourceHandle<RenderTexture> oceanDisplacement, ResourceHandle<RenderTexture> oceanDisplacementHistory, ResourceHandle<RenderTexture> oceanNormalFoamSmoothness, ResourceHandle<RenderTexture> lengthToRoughness, ResourceHandle<GraphicsBuffer> oceanBuffer)
    {
        this.oceanDisplacement = oceanDisplacement;
        this.oceanDisplacementHistory = oceanDisplacementHistory;
        this.oceanNormalFoamSmoothness = oceanNormalFoamSmoothness;
        this.lengthToRoughness = lengthToRoughness;
        this.oceanBuffer = oceanBuffer;
    }

	void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadTexture("OceanDisplacement", oceanDisplacement);
		pass.ReadTexture("OceanDisplacementHistory", oceanDisplacementHistory);
		pass.ReadTexture("OceanNormalFoamSmoothness", oceanNormalFoamSmoothness);
		pass.ReadTexture("_LengthToRoughness", lengthToRoughness);
		pass.ReadBuffer("OceanData", oceanBuffer);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}