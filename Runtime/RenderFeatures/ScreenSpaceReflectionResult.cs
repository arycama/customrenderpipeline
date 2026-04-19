using UnityEngine;
using UnityEngine.Rendering;

public readonly struct ScreenSpaceReflectionResult : IRenderPassData
{
    public readonly ResourceHandle<RenderTexture> screenSpaceReflections, opacity;
    private readonly float intensity;

    public ScreenSpaceReflectionResult(ResourceHandle<RenderTexture> screenSpaceReflections, ResourceHandle<RenderTexture> opacity, float intensity)
    {
        this.screenSpaceReflections = screenSpaceReflections;
        this.intensity = intensity;
        this.opacity = opacity;
    }

	void IRenderPassData.SetInputs(RenderPass pass)
	{
        pass.ReadTexture("ScreenSpaceReflections", screenSpaceReflections);
        pass.ReadTexture("ScreenSpaceReflectionsOpacity", opacity);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetFloat("SpecularGiStrength", intensity);
	}
}