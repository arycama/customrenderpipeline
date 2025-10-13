using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class PhysicalSkyProbe : CameraRenderFeature
{
	public override string ProfilerNameOverride => "Ggx Convolve";

	private readonly Material skyMaterial;
	private readonly EnvironmentLightingSettings environmentLighting;
	private readonly VolumetricClouds.Settings cloudSettings;
	private readonly Sky.Settings skySettings;
	private readonly Dictionary<Camera, ResourceHandle<RenderTexture>> cameraProbeHandles = new();

	public PhysicalSkyProbe(RenderGraph renderGraph, EnvironmentLightingSettings environmentLighting, VolumetricClouds.Settings cloudSettings, Sky.Settings skySettings) : base(renderGraph)
	{
        skyMaterial = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
		this.environmentLighting = environmentLighting;
		this.cloudSettings = cloudSettings;
		this.skySettings = skySettings;
	}

	protected override void Cleanup(bool disposing)
	{
		foreach (var probe in cameraProbeHandles)
			renderGraph.ReleasePersistentResource(probe.Value);
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		using var scope = renderGraph.AddProfileScope("Environment Probe Update");

		if(!cameraProbeHandles.TryGetValue(camera, out var reflectionProbeTemp))
		{
			reflectionProbeTemp = renderGraph.GetTexture(environmentLighting.Resolution, environmentLighting.Resolution, GraphicsFormat.B10G11R11_UFloatPack32, hasMips: true, autoGenerateMips: true, isPersistent: true, isExactSize: true);
			cameraProbeHandles.Add(camera, reflectionProbeTemp);
		}

		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Environment Cubemap"))
		{
			var keyword = string.Empty;
			var viewHeight = camera.transform.position.y;
			if (viewHeight > cloudSettings.StartHeight)
			{
				if (viewHeight > cloudSettings.StartHeight + cloudSettings.LayerThickness)
				{
					keyword = "ABOVE_CLOUD_LAYER";
				}
			}
			else
			{
				keyword = "BELOW_CLOUD_LAYER";
			}

			pass.Initialize(skyMaterial, skyMaterial.FindPass("Reflection Probe"), 1, keyword);
			pass.WriteTexture(reflectionProbeTemp);

			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<CloudData>();
			pass.AddRenderPassData<LightingData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<SkyTransmittanceData>();
			pass.AddRenderPassData<SkyReflectionAmbientData>();
			
			var time = (float)pass.RenderGraph.GetResource<TimeData>().Time;

			pass.SetRenderFunction((command, pass) =>
			{
				cloudSettings.SetCloudPassData(pass, time);
				pass.SetFloat("_Samples", skySettings.ReflectionSamples);
			});
		}

		renderGraph.SetResource(new EnvironmentProbeTempResult(reflectionProbeTemp));
	}
}
