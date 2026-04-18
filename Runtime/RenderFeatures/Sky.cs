using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public partial class Sky : ViewRenderFeature
{
	private static readonly int StarsId = Shader.PropertyToID("Stars");

    private readonly Settings settings;
	private readonly Material skyMaterial;

	private readonly PersistentRTHandleCache textureCache, weightCache;

	public Sky(RenderGraph renderGraph, Settings settings) : base(renderGraph)
	{
		this.settings = settings;

		skyMaterial = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
		textureCache = new(GraphicsFormat.R32_UInt, renderGraph, "Sky", isScreenTexture: true);
        weightCache = new(GraphicsFormat.R8_UNorm, renderGraph, "Sky", isScreenTexture: true);
	}

	protected override void Cleanup(bool disposing)
	{
		Object.DestroyImmediate(skyMaterial);
		textureCache.Dispose();
        weightCache.Dispose();
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		renderGraph.AddProfileBeginPass("Sky");

		var skyTemp = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);
        void RenderPass(string passName)
        {
            using (var pass = renderGraph.AddFullscreenRenderPass("Render Sky", settings.RenderSamples))
            {
                pass.Initialize(skyMaterial, viewRenderData.viewSize, 1, skyMaterial.FindPass(passName));
                pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(skyTemp);

                pass.PreventNewSubPass = true;

                pass.ReadResource<AtmospherePropertiesAndTables>();
                pass.ReadResource<AutoExposureData>();
                pass.ReadResource<CloudShadowDataResult>();
                pass.ReadResource<LightingData>();
                pass.ReadResource<ShadowData>();
                pass.ReadResource<SkyReflectionAmbientData>();
                pass.ReadResource<ViewData>();
                pass.ReadResource<SkyViewTransmittanceData>();
                pass.ReadRtHandle<CameraDepth>();

                if (pass.TryReadResource<CloudRenderResult>())
                    pass.AddKeyword("CLOUDS_ON");

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    pass.SetFloat("_Samples", data);
                });
            }
        }

        RenderPass("Render Sky");
        RenderPass("Render Scene");

		bool wasCreated = default;
		ResourceHandle<RenderTexture> current, history = default;

		// Reprojection+combine
		using (var pass = renderGraph.AddFullscreenRenderPass("Temporal", (history, wasCreated, settings.StationaryBlend, settings.MotionBlend, settings.MotionFactor, settings.DepthFactor, settings.ClampWindow, settings.MaxFrameCount, viewRenderData.viewSize, settings.StarMap, settings.StarExposure)))
		{
			(current, history, wasCreated) = textureCache.GetTextures(viewRenderData.viewSize, pass.Index, viewRenderData.viewId);
            var (weightCurrent, weightHistory, weightWasCreated) = weightCache.GetTextures(viewRenderData.viewSize, pass.Index, viewRenderData.viewId);

            pass.renderData.history = history;
			pass.renderData.wasCreated = wasCreated;

			pass.Initialize(skyMaterial, viewRenderData.viewSize, 1, skyMaterial.FindPass("Temporal"));
            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
			pass.WriteTexture(current);
			pass.WriteTexture(weightCurrent);

			pass.ReadTexture("Input", skyTemp);
			pass.ReadTexture("PreviousLuminance", history);
			pass.ReadTexture("PreviousSpeed", weightHistory);

            pass.PreventNewSubPass = true;

            pass.ReadResource<AtmospherePropertiesAndTables>();
			pass.ReadResource<TemporalAAData>();
			pass.ReadResource<AutoExposureData>();
			pass.ReadRtHandle<PreviousCameraDepth>();
			pass.ReadRtHandle<PreviousCameraVelocity>();
			pass.ReadResource<ViewData>();
			pass.ReadRtHandle<CameraVelocity>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadResource<VolumetricLighting.Result>();

            if (pass.TryReadResource<CloudRenderResult>())
                pass.AddKeyword("CLOUDS_ON");

            pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetVector("PreviousLuminanceScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.history));

				pass.SetFloat("IsFirst", data.wasCreated ? 1.0f : 0.0f);
				pass.SetFloat("StationaryBlend", data.StationaryBlend);
				pass.SetFloat("MotionBlend", data.MotionBlend);
				pass.SetFloat("MotionFactor", data.MotionFactor);
				pass.SetFloat("DepthFactor", data.DepthFactor);
				pass.SetFloat("ClampWindow", data.ClampWindow);

				pass.SetFloat("MaxFrameCount", data.MaxFrameCount);

				pass.SetInt("MaxWidth", data.Item9.x - 1);
				pass.SetInt("MaxHeight", data.Item9.y - 1);

                if (data.StarMap != null)
                    pass.SetTexture(StarsId, data.StarMap);

                pass.SetFloat("StarExposure", data.StarExposure);
            });
		}

		renderGraph.AddProfileEndPass("Sky");
	}
}
