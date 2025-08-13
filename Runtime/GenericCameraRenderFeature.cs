using System;
using UnityEngine;
using UnityEngine.Rendering;

public class GenericCameraRenderFeature : CameraRenderFeature
{
	private string profilerName;

	private Action<Camera, ScriptableRenderContext> render;

	public GenericCameraRenderFeature(RenderGraph renderGraph, string profilerName, Action<Camera, ScriptableRenderContext> render) : base(renderGraph)
	{
		this.profilerName = profilerName;
		this.render = render;
	}

	public override string ProfilerNameOverride => profilerName;

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		render(camera, context);
	}
}