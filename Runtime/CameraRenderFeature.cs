using UnityEngine;
using UnityEngine.Rendering;

/// <summary> Render feature that executes once per camera </summary>
public abstract class CameraRenderFeature : RenderFeatureBase
{
	public CameraRenderFeature(RenderGraph renderGraph) : base(renderGraph) { }

	public abstract void Render(Camera camera, ScriptableRenderContext context);
}
