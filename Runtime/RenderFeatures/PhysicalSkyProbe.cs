using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class PhysicalSkyProbe : ViewRenderFeature
{
	private readonly Material skyMaterial;
	private readonly EnvironmentLightingSettings environmentLighting;
	private readonly VolumetricClouds.Settings cloudSettings;
	private readonly Sky.Settings skySettings;
	private readonly Dictionary<int, ResourceHandle<RenderTexture>> cameraProbeHandles = new();
    private readonly EnvironmentConvolve environmentConvolve;

	public PhysicalSkyProbe(RenderGraph renderGraph, EnvironmentLightingSettings environmentLighting, VolumetricClouds.Settings cloudSettings, Sky.Settings skySettings, EnvironmentConvolve environmentConvolve) : base(renderGraph)
	{
        skyMaterial = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
		this.environmentLighting = environmentLighting;
		this.cloudSettings = cloudSettings;
		this.skySettings = skySettings;
        this.environmentConvolve = environmentConvolve;
	}

	protected override void Cleanup(bool disposing)
	{
		foreach (var probe in cameraProbeHandles)
			renderGraph.ReleasePersistentResource(probe.Value, -1);
	}

	public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
    {
		using var scope = renderGraph.AddProfileScope("Environment Probe Update");

		if(!cameraProbeHandles.TryGetValue(viewPassData.viewId, out var reflectionProbeTemp))
		{
			reflectionProbeTemp = renderGraph.GetTexture(environmentLighting.Resolution, GraphicsFormat.B10G11R11_UFloatPack32, hasMips: true, autoGenerateMips: true, isPersistent: true, isExactSize: true);
			cameraProbeHandles.Add(viewPassData.viewId, reflectionProbeTemp);
		}

		var time = (float)renderGraph.GetResource<TimeData>().time;
		using (var pass = renderGraph.AddFullscreenRenderPass("Environment Cubemap", (cloudSettings, time, skySettings, environmentLighting.Resolution, environmentLighting.Samples)))
		{
			pass.Initialize(skyMaterial, environmentLighting.Resolution, 1, skyMaterial.FindPass("Reflection Probe"), 1);

			var keyword = string.Empty;
			var viewHeight = Math.Max(0, viewPassData.position.y);
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
			pass.ReadResource<SkyViewTransmittanceData>();
			pass.ReadResource<SkyReflectionAmbientData>();
			pass.ReadResource<CloudShadowDataResult>();

            pass.SetRenderFunction(static (command, pass, data) =>
			{
				data.cloudSettings.SetCloudPassData(pass, data.time);
				pass.SetFloat("_Samples", data.Samples);
				pass.SetFloat("Resolution", data.Resolution);
            });
		}

		renderGraph.SetResource(new EnvironmentProbeTempResult(reflectionProbeTemp));
        environmentConvolve.UpdateView(viewPassData.viewId);
    }
}
