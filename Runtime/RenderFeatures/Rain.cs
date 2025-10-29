using System;
using UnityEngine;
using UnityEngine.Pool;
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
		[field: SerializeField, Range(0f, 1f)] public float WetLevel { get; private set; } = 0.5f;
		[field: SerializeField] public int Resolution { get; private set; } = 256;
		[field: SerializeField] public float Size { get; private set; } = 1;
	}

	private readonly Settings settings;
	private readonly ComputeShader rainComputeShader;

	private int previousDropletCount;
	private ResourceHandle<GraphicsBuffer> positionBuffer;
	private ResourceHandle<GraphicsBuffer> indexBuffer;

	public Rain(RenderGraph renderGraph, Settings settings) : base(renderGraph)
	{
		this.settings = settings;
		this.rainComputeShader = Resources.Load<ComputeShader>("Water/Rain");
	}

	protected override void Cleanup(bool disposing)
	{
		if (previousDropletCount != 0)
		{
			renderGraph.ReleasePersistentResource(positionBuffer);
			renderGraph.ReleasePersistentResource(indexBuffer);
		}
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var dropletCount = (int)(settings.DropletCount * settings.WetLevel * (4.0f / 3.0f) * Math.Pi * Math.Pow(settings.Radius, 3));
		if (settings.Material == null || dropletCount == 0)
			return;

		using var scope = renderGraph.AddProfileScope("Rain");

		if (dropletCount != previousDropletCount)
		{
			if (previousDropletCount != 0)
			{
				renderGraph.ReleasePersistentResource(positionBuffer);
				renderGraph.ReleasePersistentResource(indexBuffer);
			}

			positionBuffer = renderGraph.GetBuffer(dropletCount, sizeof(float) * 4, isPersistent: true);
			indexBuffer = renderGraph.GetQuadIndexBuffer(dropletCount, false);
			previousDropletCount = dropletCount;

			// New buffer, need to initialize positions
			using (var pass = renderGraph.AddComputeRenderPass("Initialize", (dropletCount, settings.Radius, settings.Velocity, settings.WindAngle, settings.WindStrength, settings.WindTurbulence)))
			{
				pass.Initialize(rainComputeShader, 0, dropletCount);

				pass.WriteBuffer("Positions", positionBuffer);
				pass.ReadBuffer("Positions", positionBuffer);

				pass.ReadResource<ViewData>();
				pass.ReadResource<FrameData>();

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					pass.SetInt("RainDropletCount", data.dropletCount);
					pass.SetFloat("RainRadius", data.Radius);
					pass.SetFloat("RainVelocity", data.Velocity);
					pass.SetFloat("WindAngle", data.WindAngle);
					pass.SetFloat("WindStrength", data.WindStrength);
					pass.SetFloat("WindTurbulence", data.WindTurbulence);
				});
			}
		}

		using (var pass = renderGraph.AddComputeRenderPass("Update", (dropletCount, settings.Radius, settings.Velocity, settings.WindAngle, settings.WindStrength, settings.WindTurbulence)))
		{
			pass.Initialize(rainComputeShader, 1, dropletCount);

			pass.WriteBuffer("Positions", positionBuffer);
			pass.ReadBuffer("Positions", positionBuffer);

			pass.ReadResource<ViewData>();
			pass.ReadResource<FrameData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetInt("RainDropletCount", data.dropletCount);
				pass.SetFloat("RainRadius", data.Radius);
				pass.SetFloat("RainVelocity", data.Velocity);
				pass.SetFloat("WindAngle", data.WindAngle);
				pass.SetFloat("WindStrength", data.WindStrength);
				pass.SetFloat("WindTurbulence", data.WindTurbulence);
			});
		}

		// TODO: Index buffer, but make some common function to build/get an index buffer 
		using (var pass = renderGraph.AddDrawProceduralIndexedRenderPass("Render", (dropletCount, settings.Radius, settings.Velocity, settings.WindAngle, settings.WindStrength, settings.WindTurbulence)))
		{
			pass.Initialize(indexBuffer, settings.Material, Float4x4.Identity);

			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepth);

			pass.ReadBuffer("Positions", positionBuffer);

			pass.ReadResource<FrameData>();
			pass.ReadResource<ViewData>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<PreviousCameraTarget>();
			pass.ReadResource<EnvironmentData>();
			pass.ReadResource<LightingData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetInt("RainDropletCount", data.dropletCount);
				pass.SetFloat("RainRadius", data.Radius);
				pass.SetFloat("RainVelocity", data.Velocity);
				pass.SetFloat("WindAngle", data.WindAngle);
				pass.SetFloat("WindStrength", data.WindStrength);
				pass.SetFloat("WindTurbulence", data.WindTurbulence);
			});
		}
	}
}
