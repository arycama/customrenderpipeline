using System;
using UnityEngine;
using UnityEngine.Rendering;

public class Rain : CameraRenderFeature
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField, Min(0)] public int DropletCount { get; private set; } = 100;
		[field: SerializeField, Min(0)] public float Radius { get; private set; } = 32;
		[field: SerializeField, Min(0)] public float Velocity { get; private set; } = 9.81f;
		[field: SerializeField, Range(0, 1)] public float WindAngle { get; private set; } = 0;
		[field: SerializeField, Range(0, Math.Pi)] public float WindTurbulence { get; private set; } = 0.1f;
		[field: SerializeField, Min(0)] public float WindStrength { get; private set; } = 0.5f;
		[field: SerializeField] public Material Material { get; private set; }
	}

	private readonly Settings settings;
	private readonly ComputeShader rainComputeShader;

	private int previousDropletCount;
	private ResourceHandle<GraphicsBuffer> positionBuffer;

	public Rain(RenderGraph renderGraph, Settings settings) : base(renderGraph)
	{
		this.settings = settings;
		this.rainComputeShader = Resources.Load<ComputeShader>("Water/Rain");
	}

	protected override void Cleanup(bool disposing)
	{
		if (previousDropletCount != 0)
			renderGraph.ReleasePersistentResource(positionBuffer);
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var dropletCount = (int)(settings.DropletCount * (4.0f / 3.0f) * Math.Pi * Math.Pow(settings.Radius, 3));
		if (settings.Material == null || dropletCount == 0)
			return;

		using var scope = renderGraph.AddProfileScope("Rain");

		if (dropletCount != previousDropletCount)
		{
			if (previousDropletCount != 0)
				renderGraph.ReleasePersistentResource(positionBuffer);

			positionBuffer = renderGraph.GetBuffer(dropletCount, sizeof(float) * 4, isPersistent: true);
			previousDropletCount = dropletCount;

			// New buffer, need to initialize positions
			using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Initialize"))
			{
				pass.Initialize(rainComputeShader, 0, dropletCount);

				pass.WriteBuffer("Positions", positionBuffer);
				pass.ReadBuffer("Positions", positionBuffer);

				pass.AddRenderPassData<ViewData>();
				pass.AddRenderPassData<FrameData>();

				pass.SetRenderFunction((command, pass) =>
				{
					pass.SetInt("RainDropletCount", dropletCount);
					pass.SetFloat("RainRadius", settings.Radius);
					pass.SetFloat("RainVelocity", settings.Velocity);
					pass.SetFloat("WindAngle", settings.WindAngle);
					pass.SetFloat("WindStrength", settings.WindStrength);
					pass.SetFloat("WindTurbulence", settings.WindTurbulence);
				});
			}
		}

		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Update"))
		{
			pass.Initialize(rainComputeShader, 1, dropletCount);

			pass.WriteBuffer("Positions", positionBuffer);
			pass.ReadBuffer("Positions", positionBuffer);

			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<FrameData>();

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetInt("RainDropletCount", dropletCount);
				pass.SetFloat("RainRadius", settings.Radius);
				pass.SetFloat("RainVelocity", settings.Velocity);
				pass.SetFloat("WindAngle", settings.WindAngle);
				pass.SetFloat("WindStrength", settings.WindStrength);
				pass.SetFloat("WindTurbulence", settings.WindTurbulence);
			});
		}

		// TODO: Index buffer, but make some common function to build/get an index buffer 
		using (var pass = renderGraph.AddRenderPass<DrawProceduralRenderPass>("Render"))
		{
			pass.Initialize(settings.Material, Float4x4.Identity, 0, 4, dropletCount, MeshTopology.Quads);

			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepth);

			pass.ReadBuffer("Positions", positionBuffer);

			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<ViewData>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<PreviousColor>();
			pass.AddRenderPassData<EnvironmentData>();
			pass.AddRenderPassData<LightingData>();

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetInt("RainDropletCount", dropletCount);
				pass.SetFloat("RainRadius", settings.Radius);
				pass.SetFloat("RainVelocity", settings.Velocity);
				pass.SetFloat("WindAngle", settings.WindAngle);
				pass.SetFloat("WindStrength", settings.WindStrength);
				pass.SetFloat("WindTurbulence", settings.WindTurbulence);
			});
		}
	}
}
