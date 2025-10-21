using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class TemporalAA : CameraRenderFeature
{
	private readonly Settings settings;
	private readonly PersistentRTHandleCache colorCache, weightCache;
	private readonly Material material;

	public TemporalAA(Settings settings, RenderGraph renderGraph) : base(renderGraph)
	{
		this.settings = settings;
		material = new Material(Shader.Find("Hidden/Temporal AA")) { hideFlags = HideFlags.HideAndDontSave };
		colorCache = new(GraphicsFormat.A2B10G10R10_UNormPack32, renderGraph, "Temporal AA Color", isScreenTexture: true);
		weightCache = new(GraphicsFormat.R8_SNorm, renderGraph, "Temporal AA Weight", isScreenTexture: true);
	}

	protected override void Cleanup(bool disposing)
	{
		colorCache.Dispose();
		weightCache.Dispose();
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		if (!settings.IsEnabled)
			return;

		var (current, history, wasCreated) = colorCache.GetTextures(camera.pixelWidth, camera.pixelHeight, camera);
		var (currentWeight, historyWeight, wasCreated1) = weightCache.GetTextures(camera.pixelWidth, camera.pixelHeight, camera);

		var result = renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
		using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Temporal AA");
		//var keyword = null;// viewData.Scale < 1.0f ? "UPSCALE" : null; // TODO: Implement
		pass.Initialize(material, 0, 1);

		pass.ReadTexture("History", history);
		pass.ReadTexture("HistoryWeight", historyWeight);
		pass.WriteTexture(result, RenderBufferLoadAction.DontCare);
		pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
		pass.WriteTexture(currentWeight, RenderBufferLoadAction.DontCare);
		pass.AddRenderPassData<TemporalAAData>();
		pass.ReadRtHandle<CameraTarget>();
		pass.ReadRtHandle<CameraStencil>();
		pass.ReadRtHandle<CameraDepth>();
		pass.ReadRtHandle<VelocityData>();
		pass.AddRenderPassData<AutoExposureData>();

		pass.SetRenderFunction((
			spatialBlur: settings.SpatialBlur,
			spatialSharpness: settings.SpatialSharpness,
			spatialSize: settings.SpatialSize,
			motionSharpness: settings.MotionSharpness * 0.8f,
			hasHistory: wasCreated ? 0.0f : 1.0f,
			stationaryBlending: settings.StationaryBlending,
			motionBlending: settings.MotionBlending,
			motionWeight: settings.MotionWeight,
			scale: 1
		),
		(command, pass, data) =>
		{
			pass.SetFloat("_SpatialBlur", data.spatialSharpness);
			pass.SetFloat("_SpatialSharpness", data.spatialSharpness);
			pass.SetFloat("_SpatialSize", data.spatialSize);
			pass.SetFloat("_MotionSharpness", data.motionSharpness);
			pass.SetFloat("_HasHistory", data.hasHistory);
			pass.SetFloat("_StationaryBlending", data.stationaryBlending);
			pass.SetFloat("_VelocityBlending", data.motionBlending);
			pass.SetFloat("_VelocityWeight", data.motionWeight);
			pass.SetFloat("_Scale", data.scale);

			pass.SetVector("HistoryScaleLimit", pass.GetScaleLimit2D(history));
			pass.SetVector("WeightHistoryScaleLimit", pass.GetScaleLimit2D(history));
		});

		renderGraph.SetRTHandle<CameraTarget>(result);
	}
}