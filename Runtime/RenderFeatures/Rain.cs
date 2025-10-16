using System;
using UnityEngine;
using UnityEngine.Rendering;

public class Rain : CameraRenderFeature
{
	[Serializable]
	public class Settings
	{
	}

	private readonly Settings settings;
	private readonly Material material;

	public Rain(RenderGraph renderGraph, Settings settings) : base(renderGraph)
	{
		this.settings = settings;
		//material = new Material(Shader.Find("Hidden/Decal Composite")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		//using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("");
		//pass.Initialize(material);

		//pass.SetRenderFunction((command, pass) =>
		//{
		//});
	}
}


