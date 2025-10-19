using System;
using UnityEngine;
using UnityEngine.Rendering;

public class Rain : CameraRenderFeature
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField, Min(0)] public int DropletCount { get; private set; } = 4096;
		[field: SerializeField, Min(0)] public float Radius { get; private set; } = 128f;
		[field: SerializeField, Min(0)] public float Lifetime { get; private set; } = 5;
		[field: SerializeField, Min(0)] public float Velocity { get; private set; } = 9.81f;
		[field: SerializeField] public Material Material { get; private set; }
	}

	private readonly Settings settings;

	public Rain(RenderGraph renderGraph, Settings settings) : base(renderGraph)
	{
		this.settings = settings;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		if (settings.Material == null || settings.DropletCount == 0)
			return;

		// TODO: Index buffer, but make some common function to build/get an index buffer 

		using var pass = renderGraph.AddRenderPass<DrawProceduralRenderPass>("Rain");
		pass.Initialize(settings.Material, Float4x4.Identity, 0, 4, settings.DropletCount, MeshTopology.Quads);

		pass.WriteTexture(renderGraph.GetResource<CameraTargetData>());
		pass.WriteDepth(renderGraph.GetResource<CameraDepthData>(), RenderTargetFlags.ReadOnlyDepth);

		pass.AddRenderPassData<FrameData>();
		pass.AddRenderPassData<ViewData>();
		pass.AddRenderPassData<CameraDepthData>();
		pass.AddRenderPassData<PreviousColor>();

		pass.SetRenderFunction((command, pass) =>
		{
			pass.SetInt("RainCount", settings.DropletCount);
			pass.SetFloat("RainRadius", settings.Radius);
			pass.SetFloat("RainVelocity", settings.Velocity);
			pass.SetFloat("RainLifetime", settings.Lifetime);
		});
	}
}
