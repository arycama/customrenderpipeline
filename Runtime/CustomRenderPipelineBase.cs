using System.Collections.Generic;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public abstract class CustomRenderPipelineBase : RenderPipeline
{
	private List<FrameRenderFeature> perFrameRenderFeatures;
	private List<CameraRenderFeature> perCameraRenderFeatures;

	protected readonly CustomRenderPipelineAsset settings;
	private readonly CommandBuffer command;

	protected readonly RenderGraph renderGraph;
	private bool isInitialized;
	private readonly bool renderDocLoaded;

	public bool IsDisposingFromRenderDoc { get; protected set; }

	public CustomRenderPipelineBase(CustomRenderPipelineAsset renderPipelineAsset)
	{
		settings = renderPipelineAsset;

		renderDocLoaded = RenderDoc.IsLoaded();

		// TODO: Can probably move some of this to the asset class
		SupportedRenderingFeatures.active = renderPipelineAsset.SupportedRenderingFeatures;
		GraphicsSettings.lightsUseLinearIntensity = true;
		GraphicsSettings.lightsUseColorTemperature = true;
		GraphicsSettings.realtimeDirectRectangularAreaLights = true;

		renderGraph = new(this);

		command = new CommandBuffer() { name = "Render Camera" };
	}

	protected override void Dispose(bool disposing)
	{
		// Could dispose in reverse order?
		foreach (var renderFeature in perFrameRenderFeatures)
			renderFeature?.Dispose();

		foreach (var renderFeature in perCameraRenderFeatures)
			renderFeature?.Dispose();

		command.Release();

		renderGraph.Dispose();
	}

    protected override void Render(ScriptableRenderContext context, Camera[] cameras) => Render(context, cameras);

    protected override void Render(ScriptableRenderContext context, List<Camera> cameras) => Render(context, cameras);

	protected abstract List<FrameRenderFeature> InitializePerFrameRenderFeatures();

	protected abstract List<CameraRenderFeature> InitializePerCameraRenderFeatures();

	private void Render(ScriptableRenderContext context, IList<Camera> cameras)
	{
		// When renderdoc is loaded, all renderTextures become un-created.. but Unity does not dispose the render pipeline until the next frame
		// To avoid errors/leaks/crashes, check to see if renderDoc.IsLoaded has changed, and skip rendering for one frame.. this allows Unity to
		// dispose the render pipeline, and then recreate it on the next frame, which will clear/re-initialize all textures..
		if (!renderDocLoaded && RenderDoc.IsLoaded())
		{
			IsDisposingFromRenderDoc = true;
			return;
		}

		GraphicsSettings.useScriptableRenderPipelineBatching = settings.UseSrpBatching;

		try
		{
			if (!isInitialized)
			{
				perFrameRenderFeatures = InitializePerFrameRenderFeatures();
				perCameraRenderFeatures = InitializePerCameraRenderFeatures();
				isInitialized = true;
			}

			using (renderGraph.AddProfileScope("Prepare Frame"))
			foreach (var frameRenderFeature in perFrameRenderFeatures)
			{
				frameRenderFeature.Render(context);
			}

			foreach (var camera in cameras)
			{
				camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

				using var renderCameraScope = renderGraph.AddProfileScope("Render Camera");

				foreach (var cameraRenderFeature in perCameraRenderFeatures)
				{
					cameraRenderFeature.Render(camera, context);
				}

				var wireOverlay = context.CreateWireOverlayRendererList(camera);

				// Draw overlay UI for the main camera. (TODO: Render to a seperate target and composite seperately for hdr compatibility
				if (camera.cameraType == CameraType.Game && camera == Camera.main)
				{
					using var pass = renderGraph.AddRenderPass<GenericRenderPass>("UI Overlay");
					pass.UseProfiler = false;

					var uiOverlay = context.CreateUIOverlayRendererList(camera);
					pass.SetRenderFunction((command, pass) =>
					{
						command.EnableShaderKeyword("UI_OVERLAY_RENDERING");
						command.DrawRendererList(uiOverlay);
						command.DisableShaderKeyword("UI_OVERLAY_RENDERING");

						command.DrawRendererList(wireOverlay);
					});
				}
			}

			renderGraph.Execute(command);

			context.ExecuteCommandBuffer(command);
			command.Clear();

			if(context.SubmitForRenderPassValidation())
				context.Submit();
		}
		finally
		{
			renderGraph.CleanupCurrentFrame();
		}
	}
}
