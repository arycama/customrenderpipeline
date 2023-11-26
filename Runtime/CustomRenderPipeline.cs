using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Experimental.Rendering;

public class CustomRenderPipeline : RenderPipeline
{
    private static readonly IndexedString blueNoise1DIds = new("STBN/Scalar/stbn_scalar_2Dx1Dx1D_128x128x64x1_");
    private static readonly IndexedString blueNoise2DIds = new("STBN/Vec2/stbn_vec2_2Dx1D_128x128x64_");
    //private static readonly int cameraTargetId = Shader.PropertyToID("_CameraTarget");
    //private static readonly int cameraDepthId = Shader.PropertyToID("_CameraDepth");
    //private static readonly int sceneTextureId = Shader.PropertyToID("_SceneTexture");
    //private static readonly int motionVectorsId = Shader.PropertyToID("_MotionVectors");

    private readonly CustomRenderPipelineAsset renderPipelineAsset;

    private readonly LightingSetup lightingSetup;
    private readonly ClusteredLightCulling clusteredLightCulling;
    private readonly VolumetricLighting volumetricLighting;
    //private readonly ObjectRenderer opaqueObjectRenderer;
    //private readonly ObjectRenderer motionVectorsRenderer;
    //private readonly ObjectRenderer transparentObjectRenderer;
    private readonly TemporalAA temporalAA;
    private readonly ConvolutionBloom convolutionBloom;
    private readonly DepthOfField depthOfField;
    private readonly Bloom bloom;
    private readonly AmbientOcclusion ambientOcclusion;

    private Material motionVectorsMaterial;
    private Material tonemappingMaterial;

    private RenderGraph renderGraph;
    private int currentFrameIndex;

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
        //opaqueObjectRenderer = new(RenderQueueRange.opaque, SortingCriteria.CommonOpaque, true, PerObjectData.None, "SRPDefaultUnlit");
        //motionVectorsRenderer = new(RenderQueueRange.opaque, SortingCriteria.CommonOpaque, false, PerObjectData.MotionVectors, "MotionVectors");
        //transparentObjectRenderer = new(RenderQueueRange.transparent, SortingCriteria.CommonTransparent, false, PerObjectData.None, "SRPDefaultUnlit");
        temporalAA = new(renderPipelineAsset.TemporalAASettings);
        convolutionBloom = new(renderPipelineAsset.ConvolutionBloomSettings);
        depthOfField = new(renderPipelineAsset.DepthOfFieldSettigns);
        bloom = new(renderPipelineAsset.BloomSettings);
        ambientOcclusion = new(renderPipelineAsset.AmbientOcclusionSettings);

        motionVectorsMaterial = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
        tonemappingMaterial = new Material(Shader.Find("Hidden/Tonemapping")) { hideFlags = HideFlags.HideAndDontSave };

        renderGraph = new RenderGraph();

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
    }

    protected override void Dispose(bool disposing)
    {
        volumetricLighting.Release();
        temporalAA.Release();

        renderGraph.Cleanup();
        renderGraph = null;
    }

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        var command = CommandBufferPool.Get("Render Camera");

        var renderGraphParams = new RenderGraphParameters()
        {
            scriptableRenderContext = context,
            commandBuffer = command,
            currentFrameIndex = currentFrameIndex
        };

        using (var recorder = renderGraph.RecordAndExecute(renderGraphParams))
        {
            foreach (var camera in cameras)
                RenderCamera(context, camera, renderGraph);
        }

        context.ExecuteCommandBuffer(command);
        CommandBufferPool.Release(command);
        context.Submit();

        currentFrameIndex++;
    }

    public class EnvSetupPassData
    {
        public int frameCount;
        public float ambientOcclusionStrength;
        public Color waterAlbedo, waterExtinction;
        public Matrix4x4 nonJitteredProjectionMatrix, previousMatrix, invVPMatrix;
    }

    public class RendererListPassData
    {
        public RendererListHandle rendererListHandle;
    }

    public class CameraMotionVectorsPassData
    {
        public TextureHandle depthTextureHandle;
        public Material motionVectorsMaterial;
    }

    public class TonemappingPassData
    {
        public TextureHandle textureHandle;
        public Camera camera;
        public float bloomStrength;
    }

    private void RenderCamera(ScriptableRenderContext context, Camera camera, RenderGraph renderGraph)
    {
        camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

        temporalAA.OnPreRender(camera, currentFrameIndex, out var jitter, out var previousMatrix);

        if (!camera.TryGetCullingParameters(out var cullingParameters))
            return;

        BeginCameraRendering(context, camera);

        cullingParameters.shadowDistance = renderPipelineAsset.ShadowSettings.ShadowDistance;
        cullingParameters.cullingOptions = CullingOptions.NeedsLighting | CullingOptions.DisablePerObjectCulling | CullingOptions.ShadowCasters;
        var cullingResults = context.Cull(ref cullingParameters);

        lightingSetup.Render(cullingResults, camera, renderGraph);

        context.SetupCameraProperties(camera);

        using (var builder = renderGraph.AddRenderPass<EnvSetupPassData>("Environment Setup", out var passData))
        {
            passData.frameCount = currentFrameIndex;
            passData.ambientOcclusionStrength = renderPipelineAsset.AmbientOcclusionSettings.Strength;
            passData.waterAlbedo = renderPipelineAsset.waterAlbedo;
            passData.waterExtinction = renderPipelineAsset.waterExtinction;
            passData.nonJitteredProjectionMatrix = camera.nonJitteredProjectionMatrix;
            passData.previousMatrix = previousMatrix;
            passData.invVPMatrix = (GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * camera.worldToCameraMatrix).inverse;

            builder.SetRenderFunc<EnvSetupPassData>((data, context) =>
            {
                context.cmd.SetGlobalVector("_AmbientLightColor", RenderSettings.ambientLight.linear);
                context.cmd.SetGlobalVector("_FogColor", RenderSettings.fogColor.linear);
                context.cmd.SetGlobalFloat("_FogStartDistance", RenderSettings.fogStartDistance);
                context.cmd.SetGlobalFloat("_FogEndDistance", RenderSettings.fogEndDistance);
                context.cmd.SetGlobalFloat("_FogDensity", RenderSettings.fogDensity);
                context.cmd.SetGlobalFloat("_FogMode", (float)RenderSettings.fogMode);
                context.cmd.SetGlobalFloat("_FogEnabled", RenderSettings.fog ? 1.0f : 0.0f);
                context.cmd.SetGlobalFloat("_AoEnabled", data.ambientOcclusionStrength > 0.0f ? 1.0f : 0.0f);
                context.cmd.SetGlobalVector("_WaterAlbedo", data.waterAlbedo.linear);
                context.cmd.SetGlobalVector("_WaterExtinction", data.waterExtinction);

                // More camera setup
                var blueNoise1D = Resources.Load<Texture2D>(blueNoise1DIds.GetString(data.frameCount % 64));
                var blueNoise2D = Resources.Load<Texture2D>(blueNoise2DIds.GetString(data.frameCount % 64));
                context.cmd.SetGlobalTexture("_BlueNoise1D", blueNoise1D);
                context.cmd.SetGlobalTexture("_BlueNoise2D", blueNoise2D);
                context.cmd.SetGlobalMatrix("_NonJitteredVPMatrix", data.nonJitteredProjectionMatrix);
                context.cmd.SetGlobalMatrix("_PreviousVPMatrix", data.previousMatrix);
                context.cmd.SetGlobalMatrix("_InvVPMatrix", data.invVPMatrix);
                context.cmd.SetGlobalInt("_FrameCount", data.frameCount);
            });
        }

        clusteredLightCulling.Render(renderGraph, camera, out var lightClusterIndices);
        volumetricLighting.Render(renderGraph, camera, currentFrameIndex, lightClusterIndices);

        // Base pass
        var cameraDepthId = renderGraph.CreateTexture(new TextureDesc(camera.pixelWidth, camera.pixelHeight) { depthBufferBits = DepthBits.Depth32, clearBuffer = true });
        var cameraTargetId = renderGraph.CreateTexture(new TextureDesc(camera.pixelWidth, camera.pixelHeight) { colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, clearBuffer = true, clearColor = camera.backgroundColor.linear });

        var opaqueRendererList = renderGraph.CreateRendererList(new(new ShaderTagId("SRPDefaultUnlit"), cullingResults, camera) { excludeObjectMotionVectors = true, sortingCriteria = SortingCriteria.CommonOpaque, renderQueueRange = RenderQueueRange.opaque });

        using (var builder = renderGraph.AddRenderPass<RendererListPassData>("Base Pass Rendering", out var passData))
        {
            builder.UseDepthBuffer(cameraDepthId, DepthAccess.ReadWrite);
            builder.UseColorBuffer(cameraTargetId, 0);
            builder.UseRendererList(opaqueRendererList);

            passData.rendererListHandle = opaqueRendererList;

            builder.SetRenderFunc<RendererListPassData>((data, context) =>
            {
                context.cmd.DrawRendererList(data.rendererListHandle);
            });
        };

        // Motion Vectors
        var motionVectorsId = renderGraph.CreateTexture(new TextureDesc(camera.pixelWidth, camera.pixelHeight) { colorFormat = GraphicsFormat.R16G16_SFloat, clearBuffer = true, clearColor = Color.clear });

        var motionVectorRendererList = renderGraph.CreateRendererList(new(new ShaderTagId("SRPDefaultUnlit"), cullingResults, camera) { sortingCriteria = SortingCriteria.CommonOpaque, rendererConfiguration = PerObjectData.MotionVectors, renderQueueRange = RenderQueueRange.opaque });

        using (var builder = renderGraph.AddRenderPass<RendererListPassData>("Motion Vector Rendering", out var passData))
        {
            builder.UseDepthBuffer(cameraDepthId, DepthAccess.ReadWrite);
            builder.UseColorBuffer(cameraTargetId, 0);
            builder.UseColorBuffer(motionVectorsId, 1);

            passData.rendererListHandle = motionVectorRendererList;

            builder.SetRenderFunc<RendererListPassData>((data, context) =>
            {
                context.cmd.DrawRendererList(data.rendererListHandle);
            });
        }

        // Camera motion vectors
        using (var builder = renderGraph.AddRenderPass<CameraMotionVectorsPassData>("Camera motion vectors", out var passData))
        {
            builder.UseDepthBuffer(cameraDepthId, DepthAccess.ReadWrite);
            builder.UseColorBuffer(motionVectorsId, 0);

            passData.depthTextureHandle = cameraDepthId;
            passData.motionVectorsMaterial = motionVectorsMaterial;

            builder.SetRenderFunc<CameraMotionVectorsPassData>((data, context) =>
            {
                context.cmd.SetGlobalTexture("_CameraDepth", data.depthTextureHandle);
                context.cmd.DrawProcedural(Matrix4x4.identity, data.motionVectorsMaterial, 0, MeshTopology.Triangles, 3);
            });
        }

        // Ambient occlusion
        //ambientOcclusion.Render(command, camera, cameraDepthId, cameraTargetId);

        // Copy scene texture
        //command.GetTemporaryRT(sceneTextureId, camera.pixelWidth, camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.RGB111110Float);
        //command.CopyTexture(cameraTargetId, sceneTextureId);
        //command.SetGlobalTexture(sceneTextureId, sceneTextureId);
        //command.SetGlobalTexture(cameraDepthId, cameraDepthId);

        // Transparent
        // command.SetRenderTarget(new RenderTargetBinding(cameraTargetId, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, cameraDepthId, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare) { flags = RenderTargetFlags.ReadOnlyDepthStencil });
        //transparentObjectRenderer.Render(ref cullingResultsWrapper.cullingResults, camera, command, ref context);

        // var taa = temporalAA.Render(camera, command, frameCount, cameraTargetId, motionVectorsId);

        //depthOfField.Render(camera, command, cameraDepthId, taa);

        //convolutionBloom.Render(command, taa, cameraTargetId);

        //var bloomResult = bloom.Render(camera, command, taa);

        var backbuffer = renderGraph.ImportBackbuffer(BuiltinRenderTextureType.CameraTarget);

        // Tonemapping
        using (var builder = renderGraph.AddRenderPass<TonemappingPassData>("Tonemapping", out var passData))
        {
            builder.UseColorBuffer(backbuffer, 0);
            passData.textureHandle = builder.ReadTexture(cameraTargetId);
            passData.camera = camera;
            passData.bloomStrength = renderPipelineAsset.BloomSettings.Strength;

            builder.SetRenderFunc<TonemappingPassData>((data, context) =>
            {
                context.cmd.SetGlobalTexture("_MainTex", data.textureHandle);
                //context.cmd.SetGlobalTexture("_MainTex", taa);
               // context.cmd.SetGlobalTexture("_Bloom", bloomResult);
                context.cmd.SetGlobalFloat("_BloomStrength", data.bloomStrength);
                context.cmd.SetGlobalFloat("_IsSceneView", data.camera.cameraType == CameraType.SceneView ? 1f : 0f);
                context.cmd.DrawProcedural(Matrix4x4.identity, tonemappingMaterial, 0, MeshTopology.Triangles, 3);
            });
        }

        // Copy final result
        //command.Blit(cameraTargetId, BuiltinRenderTextureType.CameraTarget);

        //context.ExecuteCommandBuffer(command);
        //command.Clear();

        //if (UnityEditor.Handles.ShouldRenderGizmos())
        //{
        //    context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
        //    context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
        //}

        //if (camera.cameraType == CameraType.SceneView)
        //    ScriptableRenderContext.EmitGeometryForCamera(camera);
    }
}
