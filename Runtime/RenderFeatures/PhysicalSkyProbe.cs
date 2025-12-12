using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class PhysicalSkyProbe : ViewRenderFeature
{
	public override string ProfilerNameOverride => "Ggx Convolve";

	private readonly Material skyMaterial;
	private readonly EnvironmentLightingSettings environmentLighting;
	private readonly VolumetricClouds.Settings cloudSettings;
	private readonly Sky.Settings skySettings;
	private readonly Dictionary<int, ResourceHandle<RenderTexture>> cameraProbeHandles = new();

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
			renderGraph.ReleasePersistentResource(probe.Value, -1);
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		using var scope = renderGraph.AddProfileScope("Environment Probe Update");

		if(!cameraProbeHandles.TryGetValue(viewRenderData.viewId, out var reflectionProbeTemp))
		{
			reflectionProbeTemp = renderGraph.GetTexture(environmentLighting.Resolution, GraphicsFormat.B10G11R11_UFloatPack32, hasMips: true, autoGenerateMips: true, isPersistent: true, isExactSize: true);
			cameraProbeHandles.Add(viewRenderData.viewId, reflectionProbeTemp);
		}

		var time = (float)renderGraph.GetResource<TimeData>().time;
		using (var pass = renderGraph.AddFullscreenRenderPass("Environment Cubemap", (cloudSettings, time, skySettings)))
		{
			pass.Initialize(skyMaterial, skyMaterial.FindPass("Reflection Probe"), 1);

			var keyword = string.Empty;
			var viewHeight = viewRenderData.transform.position.y;
			if (viewHeight > cloudSettings.StartHeight)
			{
				if (viewHeight > cloudSettings.StartHeight + cloudSettings.LayerThickness)
					pass.AddKeyword("ABOVE_CLOUD_LAYER");
			}
			else
			{
				pass.AddKeyword("BELOW_CLOUD_LAYER");
			}

			pass.WriteTexture(reflectionProbeTemp);

			pass.ReadResource<AtmospherePropertiesAndTables>();
			pass.ReadResource<AutoExposureData>();
			pass.ReadResource<CloudData>();
			pass.ReadResource<LightingData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<SkyTransmittanceData>();
			pass.ReadResource<SkyReflectionAmbientData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				data.cloudSettings.SetCloudPassData(pass, data.time);
				pass.SetFloat("_Samples", data.skySettings.ReflectionSamples);
			});
		}

		renderGraph.SetResource(new EnvironmentProbeTempResult(reflectionProbeTemp));
	}
}
