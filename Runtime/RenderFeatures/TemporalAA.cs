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
		using var pass = renderGraph.AddFullscreenRenderPass("Temporal AA", new TemporalAADataStruct
		(
			settings.SpatialBlur,
			settings.SpatialSharpness,
			settings.SpatialSize,
			settings.MotionSharpness * 0.8f,
			wasCreated ? 0.0f : 1.0f,
			settings.StationaryBlending,
			settings.MotionBlending,
			settings.MotionWeight,
			scale: 1,
			history,
			settings.AfterImage
		));

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
		pass.ReadRtHandle<CameraVelocity>();
		pass.AddRenderPassData<AutoExposureData>();

		pass.SetRenderFunction(static (command, pass, data) =>
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
			pass.SetFloat("AfterImage", data.afterImage);

			pass.SetVector("HistoryScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.history));
			pass.SetVector("WeightHistoryScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.history));
		});

		renderGraph.SetRTHandle<CameraTarget>(result);
	}
}

internal struct TemporalAADataStruct
{
	public float spatialBlur;
	public float spatialSharpness;
	public float spatialSize;
	public float motionSharpness;
	public float hasHistory;
	public float stationaryBlending;
	public float motionBlending;
	public float motionWeight;
	public float afterImage;
	public int scale;
	public ResourceHandle<RenderTexture> history;

	public TemporalAADataStruct(float spatialBlur, float spatialSharpness, float spatialSize, float motionSharpness, float hasHistory, float stationaryBlending, float motionBlending, float motionWeight, int scale, ResourceHandle<RenderTexture> history, float afterImage)
	{
		this.spatialBlur = spatialBlur;
		this.spatialSharpness = spatialSharpness;
		this.spatialSize = spatialSize;
		this.motionSharpness = motionSharpness;
		this.hasHistory = hasHistory;
		this.stationaryBlending = stationaryBlending;
		this.motionBlending = motionBlending;
		this.motionWeight = motionWeight;
		this.scale = scale;
		this.history = history;
		this.afterImage = afterImage;
	}
}