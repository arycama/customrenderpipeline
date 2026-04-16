using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public partial class Sky : ViewRenderFeature
{
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

		// Reprojection
		using (var pass = renderGraph.AddFullscreenRenderPass("Temporal", new SkyTemporalData(history, wasCreated, settings.StationaryBlend, settings.MotionBlend, settings.MotionFactor, settings.DepthFactor, settings.ClampWindow, settings.MaxFrameCount, viewRenderData.viewSize)))
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
			});
		}

		renderGraph.AddProfileEndPass("Sky");
	}
}

internal struct SkyTemporalData
{
	public ResourceHandle<RenderTexture> history;
	public bool wasCreated;
	public float StationaryBlend;
	public float MotionBlend;
	public float MotionFactor;
	public float DepthFactor;
	public float ClampWindow;
	public int MaxFrameCount;
	public Int2 Item9;

	public SkyTemporalData(ResourceHandle<RenderTexture> history, bool wasCreated, float stationaryBlend, float motionBlend, float motionFactor, float depthFactor, float clampWindow, int maxFrameCount, Int2 item9)
	{
		this.history = history;
		this.wasCreated = wasCreated;
		StationaryBlend = stationaryBlend;
		MotionBlend = motionBlend;
		MotionFactor = motionFactor;
		DepthFactor = depthFactor;
		ClampWindow = clampWindow;
		MaxFrameCount = maxFrameCount;
		Item9 = item9;
	}

	public override bool Equals(object obj) => obj is SkyTemporalData other && EqualityComparer<ResourceHandle<RenderTexture>>.Default.Equals(history, other.history) && wasCreated == other.wasCreated && StationaryBlend == other.StationaryBlend && MotionBlend == other.MotionBlend && MotionFactor == other.MotionFactor && DepthFactor == other.DepthFactor && ClampWindow == other.ClampWindow && MaxFrameCount == other.MaxFrameCount && EqualityComparer<Int2>.Default.Equals(Item9, other.Item9);

	public override int GetHashCode()
	{
		var hash = new System.HashCode();
		hash.Add(history);
		hash.Add(wasCreated);
		hash.Add(StationaryBlend);
		hash.Add(MotionBlend);
		hash.Add(MotionFactor);
		hash.Add(DepthFactor);
		hash.Add(ClampWindow);
		hash.Add(MaxFrameCount);
		hash.Add(Item9);
		return hash.ToHashCode();
	}

	public void Deconstruct(out ResourceHandle<RenderTexture> history, out bool wasCreated, out float stationaryBlend, out float motionBlend, out float motionFactor, out float depthFactor, out float clampWindow, out int maxFrameCount, out Int2 item9)
	{
		history = this.history;
		wasCreated = this.wasCreated;
		stationaryBlend = StationaryBlend;
		motionBlend = MotionBlend;
		motionFactor = MotionFactor;
		depthFactor = DepthFactor;
		clampWindow = ClampWindow;
		maxFrameCount = MaxFrameCount;
		item9 = Item9;
	}

	public static implicit operator (ResourceHandle<RenderTexture> history, bool wasCreated, float StationaryBlend, float MotionBlend, float MotionFactor, float DepthFactor, float ClampWindow, int MaxFrameCount, Int2)(SkyTemporalData value) => (value.history, value.wasCreated, value.StationaryBlend, value.MotionBlend, value.MotionFactor, value.DepthFactor, value.ClampWindow, value.MaxFrameCount, value.Item9);
	public static implicit operator SkyTemporalData((ResourceHandle<RenderTexture> history, bool wasCreated, float StationaryBlend, float MotionBlend, float MotionFactor, float DepthFactor, float ClampWindow, int MaxFrameCount, Int2) value) => new SkyTemporalData(value.history, value.wasCreated, value.StationaryBlend, value.MotionBlend, value.MotionFactor, value.DepthFactor, value.ClampWindow, value.MaxFrameCount, value.Item9);
}