using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Assertions;

#if UNITY_EDITOR
    using UnityEditorInternal;
#endif

public abstract class CustomRenderPipelineBase : RenderPipeline
{
    private List<FrameRenderFeature> perFrameRenderFeatures;
    private List<ViewRenderFeature> perCameraRenderFeatures;

    private readonly CommandBuffer command;

    protected readonly RenderGraph renderGraph;
    private bool isInitialized;
    private readonly bool renderDocLoaded;
    private readonly List<ViewRenderData> viewRenderDatas = new();

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

        command = new CommandBuffer();// { name = "Render Camera" };
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

    protected abstract List<ViewRenderFeature> InitializePerCameraRenderFeatures();

    /// <summary> Creates a list of render loops that will be rendered </summary>
    protected virtual void CollectViewRenderData(List<Camera> cameras, ScriptableRenderContext context, List<ViewRenderData> viewRenderDatas)
    {
        foreach (var camera in cameras)
        {
            if (!camera.TryGetCullingParameters(out var cullingParameters))
                continue;

            var screenSize = camera.targetTexture == null ? new Int2(Screen.width, Screen.height) : new Int2(camera.targetTexture.width, camera.targetTexture.height);
            var target = (RenderTargetIdentifier)(camera.targetTexture == null ? BuiltinRenderTextureType.CameraTarget : camera.targetTexture);

            // Somewhat hacky.. but this is kind of required to deal with some unity hacks so meh
            camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            viewRenderDatas.Add(new ViewRenderData(camera.ViewSize(), camera.nearClipPlane, camera.farClipPlane, camera.TanHalfFov(), camera.transform.WorldRigidTransform(), camera, context, cullingParameters, target, VRTextureUsage.None, SinglePassStereoMode.None, 1, string.Empty, GraphicsFormat.R8G8B8A8_SRGB, 1, false));
            renderGraph.RtHandleSystem.SetScreenSize(screenSize.x, screenSize.y);
        }
    }

    protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
        Assert.IsFalse(renderGraph.IsExecuting);

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

        viewRenderDatas.Clear();
        CollectViewRenderData(cameras, context, viewRenderDatas);

        using (renderGraph.AddProfileScope("Prepare Frame"))
        {
            foreach (var frameRenderFeature in perFrameRenderFeatures)
            {
                frameRenderFeature.Render(context);
            }
        }

        foreach (var viewRenderData in viewRenderDatas)
        {
            using var renderCameraScope = renderGraph.AddProfileScope("Render Camera");
            foreach (var cameraRenderFeature in perCameraRenderFeatures)
            {
                cameraRenderFeature.Render(viewRenderData);
            }

            // Draw overlay UI for the main camera. (TODO: Render to a seperate target and composite seperately for hdr compatibility
            if (viewRenderData.camera.cameraType == CameraType.Game && viewRenderData.camera == Camera.main)
            {
                var wireOverlay = context.CreateWireOverlayRendererList(viewRenderData.camera);
                var uiOverlay = context.CreateUIOverlayRendererList(viewRenderData.camera);

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

        renderGraph.Execute(command, context);

        context.ExecuteCommandBuffer(command);
        command.Clear();

#if UNITY_EDITOR
        if (renderGraph.EnableRenderPassValidation && !context.SubmitForRenderPassValidation())
        {
            Debug.LogError("Render Pass Validation Failed");
        }
        else
#endif
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
