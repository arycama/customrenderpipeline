using System;
using UnityEngine;
using UnityEngine.Rendering;

public class GenericCameraRenderFeature : CameraRenderFeature
{
	private readonly Action<Camera, ScriptableRenderContext> render;

	public GenericCameraRenderFeature(RenderGraph renderGraph, Action<Camera, ScriptableRenderContext> render) : base(renderGraph)
	{
		this.render = render;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		render(camera, context);
	}
}