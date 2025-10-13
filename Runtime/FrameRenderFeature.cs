using UnityEngine;
using UnityEngine.Rendering;

/// <summary> Render feature that executes once per frame </summary>
public abstract class FrameRenderFeature : RenderFeatureBase
{

    private Material precomputeDfgMaterial;
	public FrameRenderFeature(RenderGraph renderGraph) : base(renderGraph) { }

	public abstract void Render(ScriptableRenderContext context);
}
