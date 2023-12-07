using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

public class CustomRenderPipeline : RenderPipeline
{
    private static readonly IndexedString blueNoise1DIds = new("STBN/Scalar/stbn_scalar_2Dx1Dx1D_128x128x64x1_");
    private static readonly IndexedString blueNoise2DIds = new("STBN/Vec2/stbn_vec2_2Dx1D_128x128x64_");

    private readonly CustomRenderPipelineAsset renderPipelineAsset;

    private readonly DynamicResolution dynamicResolution;
    private CustomSampler frameTimeSampler;

    private readonly LightingSetup lightingSetup;
    private readonly ClusteredLightCulling clusteredLightCulling;
    private readonly VolumetricLighting volumetricLighting;
    private readonly ObjectRenderer opaqueObjectRenderer;
    private readonly ObjectRenderer motionVectorsRenderer;
    private readonly CameraMotionVectors cameraMotionVectors;
    private readonly AmbientOcclusion ambientOcclusion;
    private readonly ObjectRenderer transparentObjectRenderer;
    private readonly TemporalAA temporalAA;
    private readonly Bloom bloom;
    private readonly Tonemapping tonemapping;

    public CustomRenderPipeline(CustomRenderPipelineAsset renderPipelineAsset)
    {
        this.renderPipelineAsset = renderPipelineAsset;

        GraphicsSettings.useScriptableRenderPipelineBatching = renderPipelineAsset.EnableSrpBatcher;
        GraphicsSettings.lightsUseLinearIntensity = true;
        GraphicsSettings.lightsUseColorTemperature = true;
        GraphicsSettings.disableBuiltinCustomRenderTextureUpdate = true;
        GraphicsSettings.realtimeDirectRectangularAreaLights = true;

        lightingSetup = new(renderPipelineAsset.ShadowSettings);
        clusteredLightCulling = new(renderPipelineAsset.ClusteredLightingSettings);
        volumetricLighting = new(renderPipelineAsset.VolumetricLightingSettings);
        opaqueObjectRenderer = new(RenderQueueRange.opaque, SortingCriteria.CommonOpaque, true, PerObjectData.None, "SRPDefaultUnlit");
        motionVectorsRenderer = new(RenderQueueRange.opaque, SortingCriteria.CommonOpaque, false, PerObjectData.MotionVectors, "MotionVectors");
        cameraMotionVectors = new();
        ambientOcclusion = new(renderPipelineAsset.AmbientOcclusionSettings);
        transparentObjectRenderer = new(RenderQueueRange.transparent, SortingCriteria.CommonTransparent, false, PerObjectData.None, "SRPDefaultUnlit");
        temporalAA = new(renderPipelineAsset.TemporalAASettings);
        bloom = new(renderPipelineAsset.BloomSettings);
        tonemapping = new(renderPipelineAsset.BloomSettings);

        SupportedRenderingFeatures.active = new SupportedRenderingFeatures()
        {
            defaultMixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.None,
            editableMaterialRenderQueue = false,
            enlighten = false,
            lightmapBakeTypes = LightmapBakeType.Realtime,
            lightmapsModes = LightmapsMode.NonDirectional,
            lightProbeProxyVolumes = false,
            mixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.None,
            motionVectors = true,
            overridesEnvironmentLighting = false,
            overridesFog = false,
            overrideShadowmaskMessage = null,
            overridesLODBias = false,
            overridesMaximumLODLevel = false,
            overridesOtherLightingSettings = true,
            overridesRealtimeReflectionProbes = true,
            overridesShadowmask = true,
            particleSystemInstancing = true,
            receiveShadows = true,
            reflectionProbeModes = SupportedRenderingFeatures.ReflectionProbeModes.None,
            reflectionProbes = false,
            rendererPriority = false,
            rendererProbes = false,
            rendersUIOverlay = false,
            autoAmbientProbeBaking = false,
            autoDefaultReflectionProbeBaking = false,
            enlightenLightmapper = false,
            reflectionProbesBlendDistance = false,
        };

        dynamicResolution = new(renderPipelineAsset.DynamicResolutionSettings);
        frameTimeSampler = CustomSampler.Create("Frame Time", true);
    }

    protected override void Dispose(bool disposing)
    {
        lightingSetup.Release();
        clusteredLightCulling.Release();
        volumetricLighting.Release();
        temporalAA.Release();
        dynamicResolution.Release();
    }

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        dynamicResolution.Update(frameTimeSampler.GetRecorder().gpuElapsedNanoseconds);

        var command = CommandBufferPool.Get("Render Camera");
        command.BeginSample(frameTimeSampler);

        foreach (var camera in cameras)
            RenderCamera(context, camera, command);

        command.EndSample(frameTimeSampler);
        context.ExecuteCommandBuffer(command);
        CommandBufferPool.Release(command);

        context.Submit();
    }

    private void RenderCamera(ScriptableRenderContext context, Camera camera, CommandBuffer command)
    {
        camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

        temporalAA.OnPreRender(camera, command, dynamicResolution.ScaleFactor, out var previousMatrix);

        if (!camera.TryGetCullingParameters(out var cullingParameters))
            return;

        BeginCameraRendering(context, camera);

        cullingParameters.shadowDistance = renderPipelineAsset.ShadowSettings.ShadowDistance;
        cullingParameters.cullingOptions = CullingOptions.NeedsLighting | CullingOptions.DisablePerObjectCulling | CullingOptions.ShadowCasters;
        var cullingResults = context.Cull(ref cullingParameters);

        lightingSetup.Render(command, cullingResults, context, camera);

        command.SetGlobalVector("_AmbientLightColor", RenderSettings.ambientLight.linear);
        command.SetGlobalVector("_FogColor", RenderSettings.fogColor.linear);
        command.SetGlobalFloat("_FogStartDistance", RenderSettings.fogStartDistance);
        command.SetGlobalFloat("_FogEndDistance", RenderSettings.fogEndDistance);
        command.SetGlobalFloat("_FogDensity", RenderSettings.fogDensity);
        command.SetGlobalFloat("_FogMode", (float)RenderSettings.fogMode);
        command.SetGlobalFloat("_FogEnabled", RenderSettings.fog ? 1.0f : 0.0f);
        command.SetGlobalFloat("_AoEnabled", renderPipelineAsset.AmbientOcclusionSettings.Strength > 0.0f ? 1.0f : 0.0f);
        command.SetGlobalFloat("_Scale", dynamicResolution.ScaleFactor);

        command.SetGlobalVector("_WaterAlbedo", renderPipelineAsset.waterAlbedo.linear);
        command.SetGlobalVector("_WaterExtinction", renderPipelineAsset.waterExtinction);

        // More camera setup
        var blueNoise1D = Resources.Load<Texture2D>(blueNoise1DIds.GetString(Time.renderedFrameCount % 64));
        var blueNoise2D = Resources.Load<Texture2D>(blueNoise2DIds.GetString(Time.renderedFrameCount % 64));
        command.SetGlobalTexture("_BlueNoise1D", blueNoise1D);
        command.SetGlobalTexture("_BlueNoise2D", blueNoise2D);
        command.SetGlobalMatrix("_NonJitteredVPMatrix", camera.nonJitteredProjectionMatrix);
        command.SetGlobalMatrix("_PreviousVPMatrix", previousMatrix);
        command.SetGlobalMatrix("_InvVPMatrix", (GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * camera.worldToCameraMatrix).inverse);
        command.SetGlobalInt("_FrameCount", Time.renderedFrameCount);

        context.SetupCameraProperties(camera);

        clusteredLightCulling.Render(command, camera, dynamicResolution.ScaleFactor);
        volumetricLighting.Render(camera, command, dynamicResolution.ScaleFactor);

        var scaledWidth = (int)(camera.pixelWidth * dynamicResolution.ScaleFactor);
        var scaledHeight = (int)(camera.pixelHeight * dynamicResolution.ScaleFactor);

        var cameraTargetId = Shader.PropertyToID("_CameraTarget");
        command.GetTemporaryRT(cameraTargetId, new RenderTextureDescriptor(scaledWidth, scaledHeight, RenderTextureFormat.RGB111110Float));

        var cameraDepthId = Shader.PropertyToID("_CameraDepth");
        command.GetTemporaryRT(cameraDepthId, new RenderTextureDescriptor(scaledWidth, scaledHeight, RenderTextureFormat.Depth, 32));

        // Base pass
        command.SetRenderTarget(new RenderTargetBinding(cameraTargetId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, cameraDepthId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store));
        command.ClearRenderTarget(true, true, camera.backgroundColor.linear);
        opaqueObjectRenderer.Render(ref cullingResults, camera, command, ref context);

        // Motion Vectors
        var motionVectorsId = Shader.PropertyToID("_MotionVectors");
        command.GetTemporaryRT(motionVectorsId, new RenderTextureDescriptor(scaledWidth, scaledHeight, RenderTextureFormat.RGHalf));
        command.SetRenderTarget(motionVectorsId);
        command.ClearRenderTarget(false, true, Color.clear);

        command.SetRenderTarget(
            new RenderTargetBinding(new RenderTargetIdentifier[] { cameraTargetId, motionVectorsId },
            new RenderBufferLoadAction[] { RenderBufferLoadAction.Load, RenderBufferLoadAction.DontCare },
            new RenderBufferStoreAction[] { RenderBufferStoreAction.Store, RenderBufferStoreAction.Store },
            cameraDepthId, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store));

        motionVectorsRenderer.Render(ref cullingResults, camera, command, ref context);
        cameraMotionVectors.Render(command, motionVectorsId, cameraDepthId);
        ambientOcclusion.Render(command, camera, cameraDepthId, cameraTargetId, dynamicResolution.ScaleFactor);

        // Copy scene texture
        var sceneTextureId = Shader.PropertyToID("_SceneTexture");
        command.GetTemporaryRT(sceneTextureId, new RenderTextureDescriptor(scaledWidth, scaledHeight, RenderTextureFormat.RGB111110Float));
        command.CopyTexture(cameraTargetId, sceneTextureId);
        command.SetGlobalTexture(sceneTextureId, sceneTextureId);
        command.SetGlobalTexture(cameraDepthId, cameraDepthId);

        // Transparent
        command.SetRenderTarget(new RenderTargetBinding(cameraTargetId, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, cameraDepthId, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare) { flags = RenderTargetFlags.ReadOnlyDepthStencil });
        transparentObjectRenderer.Render(ref cullingResults, camera, command, ref context);

        var taa = temporalAA.Render(camera, command, cameraTargetId, motionVectorsId, dynamicResolution.ScaleFactor);
        var bloomResult = bloom.Render(camera, command, taa);
        tonemapping.Render(command, taa, bloomResult, camera.cameraType == CameraType.SceneView);

        command.ReleaseTemporaryRT(sceneTextureId);
        command.ReleaseTemporaryRT(cameraTargetId);
        command.ReleaseTemporaryRT(cameraDepthId);

        // Should release these sooner.. ideally track where they are used and release once done
        clusteredLightCulling.CameraRenderingComplete(command);
        volumetricLighting.CameraRenderComplete(command);

        context.ExecuteCommandBuffer(command);
        command.Clear();

        if (UnityEditor.Handles.ShouldRenderGizmos())
        {
            context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
            context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
        }
    }
}

public class AutoExposure
{

}