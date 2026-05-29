using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Assertions;
using UnityEngine.XR;
using System;
using UnityEngine.Pool;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

public abstract class CustomRenderPipelineBase : RenderPipeline
{
    private List<FrameRenderFeature> perFrameRenderFeatures;
    private List<ViewRenderFeature> perCameraRenderFeatures;

    private readonly CommandBuffer command;

    protected readonly RenderGraph renderGraph;
    private readonly bool renderDocLoaded;
    private readonly Dictionary<int, string> renderCameraProfileMarkers = new();

    private ViewParameter[] viewParameters = new ViewParameter[1];
    private bool isInitialized;

    public bool IsDisposingFromRenderDoc { get; protected set; }
    public bool IsDisposing { get; private set; }

    protected abstract SupportedRenderingFeatures SupportedRenderingFeatures { get; }

    protected abstract bool UseSrpBatching { get; }
    protected virtual bool RenderUiOverlay { get; } = true;
    protected virtual bool RenderWireframe { get; } = true;
    protected virtual float SdrLuminance { get; } = 250f;
    protected virtual float RenderScale { get; } = 1.0f;
    protected virtual int AntiAliasing { get; } = 1;

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

        GraphicsSettings.disableBuiltinCustomRenderTextureUpdate = true;
        LoadStoreActionDebugModeSettings.LoadStoreDebugModeEnabled = false;

        command = new CommandBuffer();// { name = "Render Camera" };
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposing = true;

        AsyncGPUReadback.WaitAllRequests();

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

        var xrDisplaySubsystems = ListPool<XRDisplaySubsystem>.Get();
        SubsystemManager.GetSubsystems(xrDisplaySubsystems);

        // Setup display output infos
        Span<DisplayData> displayOutputDatas = stackalloc DisplayData[xrDisplaySubsystems.Count + 1];

        var mainHdrSettings = HDROutputSettings.main;
        var mainColorGamut = mainHdrSettings.available ? mainHdrSettings.displayColorGamut : ColorGamut.sRGB;
        var mainPeakLuminance = mainHdrSettings.available ? mainHdrSettings.maxToneMapLuminance : SdrLuminance;
        displayOutputDatas[0] = new(mainColorGamut, mainPeakLuminance, mainHdrSettings.available);

        for (var i = 0; i < xrDisplaySubsystems.Count; i++)
        {
            var xrDisplaySubsystem = xrDisplaySubsystems[i];

            // TODO: How can we do this just one instead of every frame? We cant' do it on init since the subsystem creation is delayed
            xrDisplaySubsystem.disableLegacyRenderer = true;
            xrDisplaySubsystem.scaleOfAllRenderTargets = RenderScale;
            xrDisplaySubsystem.sRGB = true;
            xrDisplaySubsystem.textureLayout = XRDisplaySubsystem.TextureLayout.Texture2DArray;
            xrDisplaySubsystem.SetMSAALevel(AntiAliasing);

            var hdrSettings = xrDisplaySubsystem.hdrOutputSettings;
            var colorGamut = hdrSettings.available ? hdrSettings.displayColorGamut : ColorGamut.sRGB;
            var peakLuminance = hdrSettings.available ? hdrSettings.maxToneMapLuminance : SdrLuminance;
            displayOutputDatas[i + 1] = new(colorGamut, peakLuminance, hdrSettings.available);
        }

        // TODO: Convert these to spans? Will require doing two passes though, one to calculate count, and one to actually do the thing
        var viewPassDatas = ListPool<ViewPassData>.Get();
        var viewParameterCount = 0;

        foreach (var camera in cameras)
        {
            // TODO: Would it be better to use an array and an index instead for per-camera data
            var viewId = camera.GetHashCode();

            if (!renderCameraProfileMarkers.TryGetValue(viewId, out var profileMarker))
            {
                profileMarker = $"Render View ({camera.name})";
                renderCameraProfileMarkers.Add(viewId, profileMarker);
            }

            var distanceMetric = camera.transparencySortMode switch
            {
                TransparencySortMode.Perspective => DistanceMetric.Perspective,
                TransparencySortMode.Orthographic => DistanceMetric.Orthographic,
                TransparencySortMode.CustomAxis => DistanceMetric.CustomAxis,
                _ => camera.orthographic ? DistanceMetric.Orthographic : DistanceMetric.Perspective,
            };

            var sortAxis = distanceMetric == DistanceMetric.CustomAxis ? (Float3)camera.transparencySortAxis : camera.transform.WorldRotation().Forward;
            var tanHalfFov = Geometry.TanHalfFovDegrees(camera.fieldOfView);

            ViewPassData GetDisplayRenderPass(int displayIndex, int viewCount, bool isFliped, Int2 size, RenderTargetIdentifier target, GraphicsFormat format, VRTextureUsage vrUsage, in ScriptableCullingParameters cullingParameters, int mirrorBlitMode, IntPtr foveatedRenderingInfo)
            {
                return new ViewPassData
                (
                    viewParameterCount,
                    displayIndex,
                    viewCount,
                    isFliped,
                    size,
                    viewCount == 1 ? SinglePassStereoMode.None : (SystemInfo.supportsMultiview ? SinglePassStereoMode.Multiview : SinglePassStereoMode.Instancing),
                    target,
                    format,
                    vrUsage,
                    1,
                    viewId,
                    cullingParameters,
                    mirrorBlitMode,
                    foveatedRenderingInfo,
                    camera.cameraType,
                    camera.nearClipPlane,
                    camera.farClipPlane,
                    camera.transform.position,
                    camera.transform.rotation,
                    distanceMetric,
                    sortAxis,
                    new Float2(tanHalfFov * camera.aspect, tanHalfFov),
                    0,
                    camera,
                    camera.iso,
                    camera.aperture,
                    camera.shutterSpeed,
                    camera.focalLength,
                    camera.focusDistance,
                    PhysicalCameraUtility.ApertureRadius(camera.focalLength, camera.aperture),
                    PhysicalCameraUtility.EV100ToExposure(PhysicalCameraUtility.ComputeEV100(camera.aperture, camera.shutterSpeed, camera.iso))
                );
            }

            void AddViewParameter(ViewParameter viewParameter)
            {
                if (viewParameters.Length < viewParameterCount + 1)
                    Array.Resize(ref viewParameters, viewParameterCount + 1);

                viewParameters[viewParameterCount++] = viewParameter;
            }

            // Only cameras with no target texture output to the display
            if (camera.targetTexture == null && xrDisplaySubsystems.Count > 0)
            {
                for (var i = 0; i < xrDisplaySubsystems.Count; i++)
                {
                    var xrDisplaySubsystem = xrDisplaySubsystems[i];
                    var mirrorBlitMode = xrDisplaySubsystem.GetPreferredMirrorBlitMode();

                    var passCount = xrDisplaySubsystem.GetRenderPassCount();
                    for (var j = 0; j < passCount; j++)
                    {
                        xrDisplaySubsystem.GetRenderPass(j, out var renderPass);
                        xrDisplaySubsystem.GetCullingParameters(camera, renderPass.cullingPassIndex, out var cullingParameters);

                        // TODO: Any reason to not use renderTargetDesc.width and height?
                        var size = new Int2(renderPass.renderTargetScaledWidth, renderPass.renderTargetScaledHeight);
                        renderGraph.RtHandleSystem.SetScreenSize(size.x, size.y);

                        var renderTargetDesc = renderPass.renderTargetDesc;
                        var viewCount = renderPass.GetRenderParameterCount();

                        var isFlipped = renderTargetDesc.flags.HasFlag(RenderTextureCreationFlags.AllowVerticalFlip);
                        var displayRenderPass = GetDisplayRenderPass(i + 1, viewCount, isFlipped, size, renderPass.renderTarget, renderTargetDesc.graphicsFormat, renderTargetDesc.vrUsage, cullingParameters, mirrorBlitMode, renderPass.foveatedRenderingInfo);
                        viewPassDatas.Add(displayRenderPass);

                        for (var k = 0; k < viewCount; k++)
                        {
                            renderPass.GetRenderParameter(camera, k, out var renderParameter);
                            AddViewParameter(new(renderParameter.view, renderParameter.projection));
                        }
                    }
                }
            }
            else
            {
                if (!camera.TryGetCullingParameters(out var cullingParameters))
                    continue;

                var targetTexture = camera.targetTexture;
                var size = targetTexture == null ? new Int2(camera.pixelWidth, camera.pixelHeight) : new Int2(targetTexture.width, targetTexture.height);
                renderGraph.RtHandleSystem.SetScreenSize(size.x, size.y);

                var targetDesc = targetTexture == null ? default : camera.targetTexture.descriptor;
                var isFlipped = targetTexture == null ? false : targetDesc.flags.HasFlag(RenderTextureCreationFlags.AllowVerticalFlip);
                var format = targetTexture == null ? SystemInfo.GetGraphicsFormat(DefaultFormat.HDR) : targetTexture.graphicsFormat;
                var target = (RenderTargetIdentifier)(targetTexture == null ? BuiltinRenderTextureType.CameraTarget : targetTexture);

                var displayRenderPass = GetDisplayRenderPass(0, 1, isFlipped, size, target, format, VRTextureUsage.None, cullingParameters, XRMirrorViewBlitMode.None, IntPtr.Zero);
                viewPassDatas.Add(displayRenderPass);
                AddViewParameter(new(camera.worldToCameraMatrix, camera.projectionMatrix));
            }

#if UNITY_EDITOR
            if (camera.cameraType == CameraType.SceneView)
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
            else
#endif
                ScriptableRenderContext.EmitGeometryForCamera(camera);
        }

        ListPool<XRDisplaySubsystem>.Release(xrDisplaySubsystems);

        using (renderGraph.AddProfileScope("Prepare Frame"))
        {
            foreach (var frameRenderFeature in perFrameRenderFeatures)
            {
                frameRenderFeature.Render(context);
            }
        }

        foreach (var displayRenderPass in viewPassDatas)
        {
            var profileMarker = renderCameraProfileMarkers[displayRenderPass.viewId];
            ReadOnlySpan<ViewParameter> displayViewParameters = viewParameters.AsSpan(displayRenderPass.parameterStart, displayRenderPass.viewCount);

            using var renderCameraScope = renderGraph.AddProfileScope(profileMarker);
            foreach (var cameraRenderFeature in perCameraRenderFeatures)
                cameraRenderFeature.Render(in displayViewParameters, in displayRenderPass, in displayOutputDatas[displayRenderPass.displayInfoIndex], context);

            if (RenderWireframe)
            {
                var wireOverlay = context.CreateWireOverlayRendererList(displayRenderPass.camera);
                using (var pass = renderGraph.AddGenericRenderPass("Wire Overlay", wireOverlay))
                {
                    pass.SetRenderFunction(static (command, pass, data) =>
                    {
                        command.DrawRendererList(data);
                    });
                }
            }

            // Draw overlay UI for the main camera. (TODO: Render to a seperate target and composite seperately for hdr compatibility
            if (RenderUiOverlay && displayRenderPass.cameraType == CameraType.Game && displayRenderPass.camera == Camera.main)
            {
                var uiOverlay = context.CreateUIOverlayRendererList(displayRenderPass.camera);

                using var pass = renderGraph.AddGenericRenderPass("UI Overlay", (uiOverlay, displayRenderPass.camera));
                pass.UseProfiler = false;

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    command.SetRenderTarget(data.camera.targetTexture);

                    command.EnableShaderKeyword("UI_OVERLAY_RENDERING");
                    command.DrawRendererList(data.uiOverlay);
                    command.DisableShaderKeyword("UI_OVERLAY_RENDERING");
                });
            }
        }

        ListPool<ViewPassData>.Release(viewPassDatas);

        renderGraph.Execute(command, context);

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
