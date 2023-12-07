using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

public class CustomRenderPipeline : RenderPipeline
{
    private static readonly IndexedString blueNoise1DIds = new("STBN/Scalar/stbn_scalar_2Dx1Dx1D_128x128x64x1_");
    private static readonly IndexedString blueNoise2DIds = new("STBN/Vec2/stbn_vec2_2Dx1D_128x128x64_");
    private static readonly int cameraTargetId = Shader.PropertyToID("_CameraTarget");
    private static readonly int cameraDepthId = Shader.PropertyToID("_CameraDepth");
    private static readonly int sceneTextureId = Shader.PropertyToID("_SceneTexture");
    private static readonly int motionVectorsId = Shader.PropertyToID("_MotionVectors");

    private readonly CustomRenderPipelineAsset renderPipelineAsset;

    private readonly LightingSetup lightingSetup;
    private readonly ClusteredLightCulling clusteredLightCulling;
    private readonly VolumetricLighting volumetricLighting;
    private readonly ObjectRenderer opaqueObjectRenderer;
    private readonly ObjectRenderer motionVectorsRenderer;
    private readonly ObjectRenderer transparentObjectRenderer;
    private readonly TemporalAA temporalAA;
    private readonly ConvolutionBloom convolutionBloom;
    private readonly DepthOfField depthOfField;
    private readonly Bloom bloom;
    private readonly AmbientOcclusion ambientOcclusion;
    private readonly DynamicResolution dynamicResolution;

    private Dictionary<Camera, int> cameraRenderedFrameCount = new();
    private Dictionary<Camera, Matrix4x4> previousMatrices = new();

    private Material motionVectorsMaterial;
    private Material tonemappingMaterial;

    private CustomSampler frameTimeSampler;

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
        transparentObjectRenderer = new(RenderQueueRange.transparent, SortingCriteria.CommonTransparent, false, PerObjectData.None, "SRPDefaultUnlit");
        temporalAA = new(renderPipelineAsset.TemporalAASettings);
        convolutionBloom = new(renderPipelineAsset.ConvolutionBloomSettings);
        depthOfField = new(renderPipelineAsset.DepthOfFieldSettigns);
        bloom = new(renderPipelineAsset.BloomSettings);
        ambientOcclusion = new(renderPipelineAsset.AmbientOcclusionSettings);

        motionVectorsMaterial = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
        tonemappingMaterial = new Material(Shader.Find("Hidden/Tonemapping")) { hideFlags = HideFlags.HideAndDontSave };

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

        // Use a seperate frame count per camera, which we manually track
        if (!cameraRenderedFrameCount.TryGetValue(camera, out var frameCount))
            cameraRenderedFrameCount.Add(camera, 0);
        else
        {
            // Only increase when frame debugger not enabled, or we get flickering
            //if (!FrameDebugger.enabled)
            cameraRenderedFrameCount[camera] = ++frameCount;
        }

        temporalAA.OnPreRender(camera, frameCount, command, dynamicResolution.ScaleFactor);

        if (!previousMatrices.TryGetValue(camera, out var previousMatrix))
        {
            previousMatrix = camera.nonJitteredProjectionMatrix;
            previousMatrices.Add(camera, previousMatrix);
        }
        else
        {
            previousMatrices[camera] = camera.nonJitteredProjectionMatrix;
        }

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
        var blueNoise1D = Resources.Load<Texture2D>(blueNoise1DIds.GetString(frameCount % 64));
        var blueNoise2D = Resources.Load<Texture2D>(blueNoise2DIds.GetString(frameCount % 64));
        command.SetGlobalTexture("_BlueNoise1D", blueNoise1D);
        command.SetGlobalTexture("_BlueNoise2D", blueNoise2D);
        command.SetGlobalMatrix("_NonJitteredVPMatrix", camera.nonJitteredProjectionMatrix);
        command.SetGlobalMatrix("_PreviousVPMatrix", previousMatrix);
        command.SetGlobalMatrix("_InvVPMatrix", (GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * camera.worldToCameraMatrix).inverse);
        command.SetGlobalInt("_FrameCount", frameCount);

        context.SetupCameraProperties(camera);

        clusteredLightCulling.Render(command, camera, dynamicResolution.ScaleFactor);
        volumetricLighting.Render(camera, command, frameCount, dynamicResolution.ScaleFactor);

        var scaledWidth = (int)(camera.pixelWidth * dynamicResolution.ScaleFactor);
        var scaledHeight = (int)(camera.pixelHeight * dynamicResolution.ScaleFactor);

        command.GetTemporaryRT(cameraTargetId, new RenderTextureDescriptor(scaledWidth, scaledHeight, RenderTextureFormat.RGB111110Float));
        command.GetTemporaryRT(cameraDepthId, new RenderTextureDescriptor(scaledWidth, scaledHeight, RenderTextureFormat.Depth, 32));

        // Base pass
        command.SetRenderTarget(new RenderTargetBinding(cameraTargetId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, cameraDepthId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store));
        command.ClearRenderTarget(true, true, camera.backgroundColor.linear);
        opaqueObjectRenderer.Render(ref cullingResults, camera, command, ref context);

        // Motion Vectors
        command.GetTemporaryRT(motionVectorsId, new RenderTextureDescriptor(scaledWidth, scaledHeight, RenderTextureFormat.RGHalf));
        command.SetRenderTarget(motionVectorsId);
        command.ClearRenderTarget(false, true, Color.clear);

        command.SetRenderTarget(
            new RenderTargetBinding(new RenderTargetIdentifier[] { cameraTargetId, motionVectorsId },
            new RenderBufferLoadAction[] { RenderBufferLoadAction.Load, RenderBufferLoadAction.DontCare },
            new RenderBufferStoreAction[] { RenderBufferStoreAction.Store, RenderBufferStoreAction.Store },
            cameraDepthId, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store));

        motionVectorsRenderer.Render(ref cullingResults, camera, command, ref context);

        // Camera motion vectors
        using (var profilerScope = command.BeginScopedSample("Camera Motion Vectors"))
        {
            command.SetRenderTarget(new RenderTargetBinding(motionVectorsId, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, cameraDepthId, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store) { flags = RenderTargetFlags.ReadOnlyDepthStencil });
            command.SetGlobalTexture("_CameraDepth", cameraDepthId);
            command.DrawProcedural(Matrix4x4.identity, motionVectorsMaterial, 0, MeshTopology.Triangles, 3);
        }

        // Ambient occlusion
        ambientOcclusion.Render(command, camera, cameraDepthId, cameraTargetId, dynamicResolution.ScaleFactor);

        // Copy scene texture
        command.GetTemporaryRT(sceneTextureId, new RenderTextureDescriptor(scaledWidth, scaledHeight, RenderTextureFormat.RGB111110Float));
        command.CopyTexture(cameraTargetId, sceneTextureId);
        command.SetGlobalTexture(sceneTextureId, sceneTextureId);
        command.SetGlobalTexture(cameraDepthId, cameraDepthId);

        // Transparent
        command.SetRenderTarget(new RenderTargetBinding(cameraTargetId, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, cameraDepthId, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare) { flags = RenderTargetFlags.ReadOnlyDepthStencil });
        transparentObjectRenderer.Render(ref cullingResults, camera, command, ref context);

        var taa = temporalAA.Render(camera, command, frameCount, cameraTargetId, motionVectorsId, dynamicResolution.ScaleFactor);

        //depthOfField.Render(camera, command, cameraDepthId, taa);

        //convolutionBloom.Render(command, taa, cameraTargetId);

        var bloomResult = bloom.Render(camera, command, taa);

        using (var profilerScope = command.BeginScopedSample("Tonemapping"))
        {
            command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            command.SetGlobalTexture("_MainTex", taa);
            command.SetGlobalTexture("_Bloom", bloomResult);
            command.SetGlobalFloat("_BloomStrength", renderPipelineAsset.BloomSettings.Strength);
            command.SetGlobalFloat("_IsSceneView", camera.cameraType == CameraType.SceneView ? 1f : 0f);
            command.DrawProcedural(Matrix4x4.identity, tonemappingMaterial, 0, MeshTopology.Triangles, 3);
        }

        // Copy final result
        //command.Blit(cameraTargetId, BuiltinRenderTextureType.CameraTarget);

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

        //if (camera.cameraType == CameraType.SceneView)
        //    ScriptableRenderContext.EmitGeometryForCamera(camera);
    }
}
