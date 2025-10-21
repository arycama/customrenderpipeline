using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditorInternal;
#endif

public abstract class CustomRenderPipelineBase : RenderPipeline
{
    private List<FrameRenderFeature> perFrameRenderFeatures;
    private List<CameraRenderFeature> perCameraRenderFeatures;

    private readonly CommandBuffer command;

    protected readonly RenderGraph renderGraph;
    private bool isInitialized;
    private readonly bool renderDocLoaded;

    public bool IsDisposingFromRenderDoc { get; protected set; }

    protected abstract SupportedRenderingFeatures SupportedRenderingFeatures { get; }

    protected abstract bool UseSrpBatching { get; }

    public CustomRenderPipelineBase()
    {
#if UNITY_EDITOR
		renderDocLoaded = RenderDoc.IsLoaded();
#endif

		// TODO: Can probably move some of this to the asset class
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

	protected abstract List<FrameRenderFeature> InitializePerFrameRenderFeatures();

	protected abstract List<CameraRenderFeature> InitializePerCameraRenderFeatures();

	protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
#if UNITY_EDITOR
		// When renderdoc is loaded, all renderTextures become un-created.. but Unity does not dispose the render pipeline until the next frame
		// To avoid errors/leaks/crashes, check to see if renderDoc.IsLoaded has changed, and skip rendering for one frame.. this allows Unity to
		// dispose the render pipeline, and then recreate it on the next frame, which will clear/re-initialize all textures..
		if (!renderDocLoaded && RenderDoc.IsLoaded())
        {
            IsDisposingFromRenderDoc = true;
            return;
        }
#endif

		GraphicsSettings.useScriptableRenderPipelineBatching = UseSrpBatching;

		if (!isInitialized)
		{
			perFrameRenderFeatures = InitializePerFrameRenderFeatures();
			perCameraRenderFeatures = InitializePerCameraRenderFeatures();
			isInitialized = true;
		}

		using (renderGraph.AddProfileScope("Prepare Frame"))
		{
			foreach (var frameRenderFeature in perFrameRenderFeatures)
			{
				frameRenderFeature.Render(context);
			}
		}

		foreach (var camera in cameras)
        {
			camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

            using var renderCameraScope = renderGraph.AddProfileScope("Render Camera");

                renderGraph.RtHandleSystem.SetScreenSize(camera.pixelWidth, camera.pixelHeight);

            foreach (var cameraRenderFeature in perCameraRenderFeatures)
            {
                cameraRenderFeature.Render(camera, context);
            }

            var wireOverlay = context.CreateWireOverlayRendererList(camera);

            // Draw overlay UI for the main camera. (TODO: Render to a seperate target and composite seperately for hdr compatibility
            if (camera.cameraType == CameraType.Game && camera == Camera.main)
            {
				var uiOverlay = context.CreateUIOverlayRendererList(camera);

				using var pass = renderGraph.AddGenericRenderPass("UI Overlay", (uiOverlay, wireOverlay));
                pass.UseProfiler = false;

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    command.EnableShaderKeyword("UI_OVERLAY_RENDERING");
                    command.DrawRendererList(data.uiOverlay);
                    command.DisableShaderKeyword("UI_OVERLAY_RENDERING");

                    command.DrawRendererList(data.wireOverlay);
                });
            }
        }

		renderGraph.Execute(command);

        context.ExecuteCommandBuffer(command);
		command.Clear();
		context.Submit();

        renderGraph.CleanupCurrentFrame();
    }
}

public abstract class CustomRenderPipelineBase<T> : CustomRenderPipelineBase where T : CustomRenderPipelineAssetBase
{
    protected readonly T asset;

    protected override SupportedRenderingFeatures SupportedRenderingFeatures => asset.SupportedRenderingFeatures;

    protected override bool UseSrpBatching => asset.UseSrpBatching;

    public CustomRenderPipelineBase(T asset) : base()
    {
        this.asset = asset;
        SupportedRenderingFeatures.active = SupportedRenderingFeatures;
    }
}
