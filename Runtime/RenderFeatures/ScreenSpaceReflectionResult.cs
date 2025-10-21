using UnityEngine;
using UnityEngine.Rendering;

public readonly struct ScreenSpaceReflectionResult : IRenderPassData
{
    public ResourceHandle<RenderTexture> ScreenSpaceReflections { get; }
    private readonly float intensity;

    public ScreenSpaceReflectionResult(ResourceHandle<RenderTexture> screenSpaceReflections, float intensity)
    {
        ScreenSpaceReflections = screenSpaceReflections;
        this.intensity = intensity;
    }

	void IRenderPassData.SetInputs(RenderPass pass)
	{
        pass.ReadTexture("ScreenSpaceReflections", ScreenSpaceReflections);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetVector("ScreenSpaceReflectionsScaleLimit", pass.GetScaleLimit2D(ScreenSpaceReflections));
		pass.SetFloat("SpecularGiStrength", intensity);
	}
}