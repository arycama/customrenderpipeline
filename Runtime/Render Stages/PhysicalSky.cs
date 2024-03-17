using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class PhysicalSky
    {
        /// <summary>
        /// List of look at matrices for cubemap faces.
        /// Ref: https://msdn.microsoft.com/en-us/library/windows/desktop/bb204881(v=vs.85).aspx
        /// </summary>
        static public readonly Vector3[] lookAtList =
        {
            new Vector3(1.0f, 0.0f, 0.0f),
            new Vector3(-1.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, -1.0f, 0.0f),
            new Vector3(0.0f, 0.0f, 1.0f),
            new Vector3(0.0f, 0.0f, -1.0f),
        };

        /// <summary>
        /// List of up vectors for cubemap faces.
        /// Ref: https://msdn.microsoft.com/en-us/library/windows/desktop/bb204881(v=vs.85).aspx
        /// </summary>
        static public readonly Vector3[] upVectorList =
        {
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, 0.0f, -1.0f),
            new Vector3(0.0f, 0.0f, 1.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
        };

        [Serializable]
        public class Settings
        {
            [field: Header("Atmosphere Properties")]
            [field: SerializeField, Range(0.0f, 1.0f)] public float EarthScale { get; private set; } = 1.0f;
            [field: SerializeField] public Vector3 RayleighScatter { get; private set; } = new Vector3(5.802e-6f, 13.558e-6f, 33.1e-6f);
            [field: SerializeField] public float RayleighHeight { get; private set; } = 8000.0f;
            [field: SerializeField] public float MieScatter { get; private set; } = 3.996e-6f;
            [field: SerializeField] public float MieAbsorption { get; private set; } = 4.4e-6f;
            [field: SerializeField] public float MieHeight { get; private set; } = 1200.0f;
            [field: SerializeField, Range(-1.0f, 1.0f)] public float MiePhase { get; private set; } = 0.8f;
            [field: SerializeField] public Vector3 OzoneAbsorption { get; private set; } = new Vector3(0.65e-6f, 1.881e-6f, 0.085e-6f);
            [field: SerializeField] public float OzoneWidth { get; private set; } = 15000.0f;
            [field: SerializeField] public float OzoneHeight { get; private set; } = 25000.0f;
            [field: SerializeField] public float PlanetRadius { get; private set; } = 6360000.0f;
            [field: SerializeField] public float AtmosphereHeight { get; private set; } = 100000.0f;
            [field: SerializeField] public Color GroundColor { get; private set; } = Color.grey;

            [field: Header("Transmittance Lookup")]
            [field: SerializeField] public int TransmittanceWidth { get; private set; } = 128;
            [field: SerializeField] public int TransmittanceHeight { get; private set; } = 64;
            [field: SerializeField] public int TransmittanceSamples { get; private set; } = 64;

            [field: Header("CDF Lookup")]
            [field: SerializeField] public int CdfWidth { get; private set; } = 128;
            [field: SerializeField] public int CdfHeight { get; private set; } = 64;
            [field: SerializeField] public int CdfDepth { get; private set; } = 64;
            [field: SerializeField] public int CdfSamples { get; private set; } = 64;

            [field: Header("Multi Scatter Lookup")]
            [field: SerializeField] public int MultiScatterWidth { get; private set; } = 32;
            [field: SerializeField] public int MultiScatterHeight { get; private set; } = 32;
            [field: SerializeField] public int MultiScatterSamples { get; private set; } = 64;

            [field: Header("Ambient Ground Lookup")]
            [field: SerializeField] public int AmbientGroundWidth { get; private set; } = 32;
            [field: SerializeField] public int AmbientGroundSamples { get; private set; } = 64;

            [field: Header("Ambient Sky Lookup")]
            [field: SerializeField] public int AmbientSkyWidth { get; private set; } = 128;
            [field: SerializeField] public int AmbientSkyHeight { get; private set; } = 64;
            [field: SerializeField] public int AmbientSkySamples { get; private set; } = 64;

            [field: Header("Reflection Probe")]
            [field: SerializeField] public int ReflectionResolution { get; private set; } = 128;
            [field: SerializeField] public int ReflectionSamples { get; private set; } = 16;

            [field: Header("Rendering")]
            [field: SerializeField] public int RenderSamples { get; private set; } = 32;

            [field: Header("Convolution")]
            [field: SerializeField] public int ConvolutionSamples { get; private set; } = 64;

            [field: Header("Temporal")]
            [field: SerializeField, Range(0, 32)] public int MaxFrameCount { get; private set; } = 16;
            [field: SerializeField, Range(0.0f, 1.0f)] public float StationaryBlend { get; private set; } = 0.95f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float MotionBlend { get; private set; } = 0.0f;
            [field: SerializeField, Min(0.0f)] public float MotionFactor { get; private set; } = 6000.0f;
            [field: SerializeField, Min(0.0f)] public float DepthFactor { get; private set; } = 0.0f;
            [field: SerializeField, Min(0.0f)] public float ClampWindow { get; private set; } = 1.0f;

            [field: Header("Spatial")]
            [field: SerializeField, Range(0, 32)] public int SpatialSamples { get; private set; } = 4;
            [field: SerializeField, Min(0.0f)] public float SpatialDepthFactor { get; private set; } = 0.1f;
            [field: SerializeField, Min(0.0f)] public float SpatialBlurSigma { get; private set; } = 0.1f;
            [field: SerializeField, Range(0, 32)] public int SpatialBlurFrames { get; private set; } = 8;

            [field: NonSerialized] public int Version { get; private set; }
        }

        private readonly RenderGraph renderGraph;
        private readonly Settings settings;
        private readonly Material skyMaterial;
        private readonly Material ggxConvolutionMaterial;
        private readonly RTHandle transmittance, cdf, multiScatter, groundAmbient, skyAmbient, weightedDepth;
        private int version = -1;

        private PersistentRTHandleCache textureCache, depthCache, frameCountCache;

        public PhysicalSky(RenderGraph renderGraph, Settings settings)
        {
            this.renderGraph = renderGraph;
            this.settings = settings;

            skyMaterial = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
            ggxConvolutionMaterial = new Material(Shader.Find("Hidden/Ggx Convolve")) { hideFlags = HideFlags.HideAndDontSave };
            textureCache = new(GraphicsFormat.B10G11R11_UFloatPack32, renderGraph, "Physical Sky");
            depthCache = new(GraphicsFormat.R32_SFloat, renderGraph, "Physical Sky Depth");
            frameCountCache = new(GraphicsFormat.R8_UNorm, renderGraph, "Physical Sky Frame Count");

            transmittance = renderGraph.GetTexture(settings.TransmittanceWidth, settings.TransmittanceHeight, GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
            weightedDepth = renderGraph.GetTexture(settings.TransmittanceWidth, settings.TransmittanceHeight, GraphicsFormat.R32_SFloat, isPersistent: true);
            cdf = renderGraph.GetTexture(settings.CdfWidth, settings.CdfHeight, GraphicsFormat.R32_SFloat, dimension: TextureDimension.Tex3D, volumeDepth: settings.CdfDepth, isPersistent: true);
            multiScatter = renderGraph.GetTexture(settings.MultiScatterWidth, settings.MultiScatterHeight, GraphicsFormat.B10G11R11_UFloatPack32, true, isPersistent: true);
            groundAmbient = renderGraph.GetTexture(settings.AmbientGroundWidth, 1, GraphicsFormat.B10G11R11_UFloatPack32, true, isPersistent: true);
            skyAmbient = renderGraph.GetTexture(settings.AmbientSkyWidth, settings.AmbientSkyHeight, GraphicsFormat.B10G11R11_UFloatPack32, true, isPersistent: true);
        }

        public LookupTableResult GenerateLookupTables()
        {
            var atmospherePropertiesBuffer = renderGraph.SetConstantBuffer(new AtmosphereProperties(settings));
            var transmittanceRemap = GraphicsUtilities.HalfTexelRemap(settings.TransmittanceWidth, settings.TransmittanceHeight);
            var multiScatterRemap = GraphicsUtilities.HalfTexelRemap(settings.MultiScatterWidth, settings.MultiScatterHeight);
            var groundAmbientRemap = GraphicsUtilities.HalfTexelRemap(settings.AmbientGroundWidth);
            var skyAmbientRemap = GraphicsUtilities.HalfTexelRemap(settings.AmbientSkyWidth, settings.AmbientSkyHeight);

            var result = new LookupTableResult(atmospherePropertiesBuffer, transmittance, weightedDepth, multiScatter, groundAmbient, cdf, skyAmbient, transmittanceRemap, multiScatterRemap, skyAmbientRemap, groundAmbientRemap, new Vector3(settings.CdfWidth, settings.CdfHeight, settings.CdfDepth));

            if (version >= settings.Version)
                return result;

            version = settings.Version;

            // Generate transmittance LUT
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Atmosphere Transmittance"))
            {
                pass.Initialize(skyMaterial, 0);
                pass.WriteTexture(transmittance, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(weightedDepth, RenderBufferLoadAction.DontCare);
                result.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    result.SetProperties(pass, command);
                    pass.SetFloat(command, "_Samples", settings.TransmittanceSamples);
                    pass.SetVector(command, "_ScaleOffset", new Vector4(1.0f / (settings.TransmittanceWidth - 1.0f), 1.0f / (settings.TransmittanceHeight - 1.0f), -0.5f / (settings.TransmittanceWidth - 1.0f), -0.5f / (settings.TransmittanceHeight - 1.0f)));
                });
            }

            // CDF
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Atmosphere CDF"))
            {
                var primitiveCount = MathUtils.DivRoundUp(settings.CdfDepth, 32);
                pass.Initialize(skyMaterial, 1, primitiveCount);
                pass.DepthSlice = -1;
                pass.WriteTexture(cdf, RenderBufferLoadAction.DontCare);
                result.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    result.SetProperties(pass, command);
                    pass.SetFloat(command, "_Samples", settings.CdfSamples);
                    pass.SetFloat(command, "_ColorChannelScale", (settings.CdfWidth - 1.0f) / (settings.CdfWidth / 3.0f));
                    //pass.SetVector(command, "_Scale", new Vector3(MathUtils.Rcp(settings.CdfWidth - 1.0f), MathUtils.Rcp(settings.CdfHeight - 1.0f), MathUtils.Rcp(settings.CdfDepth - 1.0f)));
                    //pass.SetVector(command, "_Offset", new Vector3(MathUtils.Rcp(-2.0f * settings.CdfWidth + 2.0f), MathUtils.Rcp(-2.0f * settings.CdfHeight + 2.0f), MathUtils.Rcp(-2.0f * settings.CdfDepth + 2.0f)));

                    pass.SetVector(command, "_SkyCdfSize", new Vector3(settings.CdfWidth, settings.CdfHeight, settings.CdfDepth));

                    pass.SetVector(command, "_CdfSize", new Vector3(settings.CdfWidth, settings.CdfHeight, settings.CdfDepth));
                });
            }

            // Generate multi-scatter LUT
            var computeShader = Resources.Load<ComputeShader>("PhysicalSky");
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Atmosphere Multi Scatter"))
            {
                pass.Initialize(computeShader, 0, settings.MultiScatterWidth, settings.MultiScatterHeight, 1, false);
                pass.WriteTexture("_MultiScatterResult", multiScatter);
                result.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    result.SetProperties(pass, command);
                    pass.SetFloat(command, "_Samples", settings.MultiScatterSamples);
                    pass.SetVector(command, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(settings.MultiScatterWidth, settings.MultiScatterHeight));
                });
            }

            // Ambient Ground LUT
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Atmosphere Ambient Ground"))
            {
                pass.Initialize(computeShader, 1, settings.AmbientGroundWidth, 1, 1, false);
                pass.WriteTexture("_AmbientGroundResult", groundAmbient);
                result.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    result.SetProperties(pass, command);
                    pass.SetFloat(command, "_Samples", settings.AmbientGroundSamples);
                    pass.SetVector(command, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(settings.AmbientGroundWidth, 1));
                });
            }

            // Ambient Sky LUT
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Atmosphere Ambient Sky"))
            {
                pass.Initialize(computeShader, 2, settings.AmbientSkyWidth, settings.AmbientSkyHeight, 1, false);
                pass.WriteTexture("_AmbientSkyResult", skyAmbient);
                result.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    result.SetProperties(pass, command);
                    pass.SetFloat(command, "_Samples", settings.AmbientSkySamples);
                    pass.SetVector(command, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(settings.AmbientSkyWidth, settings.AmbientSkyHeight));
                });
            }

            return result;
        }

        public Result GenerateData(Vector3 viewPosition, LightingSetup.Result lightingSetupResult, BufferHandle exposureBuffer, LookupTableResult lookupTableResult)
        {
            // Generate Reflection probe
            var skyReflection = renderGraph.GetTexture(settings.ReflectionResolution, settings.ReflectionResolution, GraphicsFormat.B10G11R11_UFloatPack32, dimension: TextureDimension.Cube, hasMips: true, autoGenerateMips: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Sky Reflection"))
            {
                pass.Initialize(skyMaterial, 2);
                pass.WriteTexture(skyReflection, RenderBufferLoadAction.DontCare);
                pass.DepthSlice = RenderTargetIdentifier.AllDepthSlices;

                lightingSetupResult.SetInputs(pass);
                lookupTableResult.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    lightingSetupResult.SetProperties(pass, command);

                    pass.SetFloat(command, "_Samples", settings.ReflectionSamples);
                    pass.SetVector(command, "_ViewPosition", viewPosition);
                    pass.SetConstantBuffer(command, "Exposure", exposureBuffer);
                    lookupTableResult.SetProperties(pass, command);

                    var array = ArrayPool<Matrix4x4>.Get(6);

                    for (var i = 0; i < 6; i++)
                    {
                        var rotation = Quaternion.LookRotation(lookAtList[i], upVectorList[i]);
                        var viewToWorld = Matrix4x4.TRS(viewPosition, rotation, Vector3.one);
                        array[i] = Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(settings.ReflectionResolution, settings.ReflectionResolution, Vector2.zero, 90.0f, 1.0f, viewToWorld, true);
                    }

                    pass.SetMatrixArray(command, "_PixelToWorldViewDirs", array);
                    ArrayPool<Matrix4x4>.Release(array);
                });
            }

            // Generate ambient probe
            var ambientBufferTemp = renderGraph.GetBuffer(7, sizeof(float) * 4, GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Atmosphere Ambient Probe"))
            {
                pass.Initialize(Resources.Load<ComputeShader>("PhysicalSky"), 3, 1, 1, 1, false);

                // Prefiltered importance sampling
                // Use lower MIP-map levels for fetching samples with low probabilities
                // in order to reduce the variance.
                // Ref: http://http.developer.nvidia.com/GPUGems3/gpugems3_ch20.html
                //
                // - OmegaS: Solid angle associated with the sample
                // - OmegaP: Solid angle associated with the texel of the cubemap
                var sampleCount = 256; // Must match PhysicalSky.compute
                var rcpOmegaP = 6.0f * settings.ReflectionResolution * settings.ReflectionResolution / (4.0f * Mathf.PI);
                var pdf = 1.0f / (4.0f * Mathf.PI);
                var omegaS = 1.0f / sampleCount / pdf;
                var mipLevel = 0.5f * Mathf.Log(omegaS * rcpOmegaP, 2.0f);

                pass.ReadTexture("_AmbientProbeInputCubemap", skyReflection);
                pass.WriteBuffer("_AmbientProbeOutputBuffer", ambientBufferTemp);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    pass.SetFloat(command, "_MipLevel", mipLevel);
                });
            }

            // Copy ambient
            var ambientBuffer = renderGraph.GetBuffer(7, sizeof(float) * 4, GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination);
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Atmosphere Ambient Probe Copy"))
            {
                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    command.CopyBuffer(ambientBufferTemp, ambientBuffer);
                });
            }

            // Generate Reflection probe
            var reflectionProbe = renderGraph.GetTexture(settings.ReflectionResolution, settings.ReflectionResolution, GraphicsFormat.B10G11R11_UFloatPack32, dimension: TextureDimension.Cube, hasMips: true);
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Sky Reflection Copy"))
            {
                pass.WriteTexture(reflectionProbe);
                pass.ReadTexture("_SkyReflection", skyReflection);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    command.CopyTexture(skyReflection, reflectionProbe);
                });
            }

            var invOmegaP = 6.0f * settings.ReflectionResolution * settings.ReflectionResolution / (4.0f * Mathf.PI);

            const int mipLevels = 6;
            for (var i = 1; i < 7; i++)
            {
                using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Sky Reflection Convolution"))
                {
                    pass.Initialize(ggxConvolutionMaterial, 0);
                    pass.WriteTexture(reflectionProbe, RenderBufferLoadAction.DontCare);
                    pass.ReadTexture("_SkyReflection", skyReflection);
                    pass.DepthSlice = RenderTargetIdentifier.AllDepthSlices;
                    pass.MipLevel = i;
                    var index = i;

                    var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                    {
                        pass.SetFloat(command, "_Samples", settings.ConvolutionSamples);

                        var array = ArrayPool<Matrix4x4>.Get(6);

                        for (var j = 0; j < 6; j++)
                        {
                            var resolution = settings.ReflectionResolution >> index;
                            var rotation = Quaternion.LookRotation(lookAtList[j], upVectorList[j]);
                            var viewToWorld = Matrix4x4.TRS(viewPosition, rotation, Vector3.one);
                            array[j] = Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(resolution, resolution, Vector2.zero, 90.0f, 1.0f, viewToWorld, true);
                        }

                        pass.SetMatrixArray(command, "_PixelToWorldViewDirs", array);
                        ArrayPool<Matrix4x4>.Release(array);

                        pass.SetFloat(command, "_Level", index);
                        pass.SetFloat(command, "_InvOmegaP", invOmegaP);

                        var perceptualRoughness = Mathf.Clamp01(index / (float)mipLevels);
                        var mipPerceptualRoughness = Mathf.Clamp01(1.7f / 1.4f - Mathf.Sqrt(2.89f / 1.96f - (2.8f / 1.96f) * perceptualRoughness));
                        var mipRoughness = mipPerceptualRoughness * mipPerceptualRoughness;
                        pass.SetFloat(command, "_Roughness", mipRoughness);
                    });
                }
            }

            // Specular convolution
            return new Result(ambientBuffer, reflectionProbe, lookupTableResult);
        }

        public void Render(RTHandle target, RTHandle depth, BufferHandle exposureBuffer, int width, int height, float fov, float aspect, Matrix4x4 viewToWorld, LightingSetup.Result lightingSetupResult, PhysicalSky.Result atmosphereData, Vector2 jitter, IRenderPassData commonPassData, RTHandle clouds, RTHandle cloudDepth, VolumetricClouds.CloudShadowDataResult cloudShadowData, Camera camera, CullingResults cullingResults, PhysicalSky.LookupTableResult lookupData, RTHandle velocity)
        {
            var skyTemp = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

            Color lightColor0 = Color.clear, lightColor1 = Color.clear;
            Vector3 lightDirection0 = Vector3.up, lightDirection1 = Vector3.up;

            // Find first 2 directional lights
            var dirLightCount = 0;
            for (var i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                var light = cullingResults.visibleLights[i];
                if (light.lightType != LightType.Directional)
                    continue;

                dirLightCount++;

                if (dirLightCount == 1)
                {
                    lightDirection0 = -light.localToWorldMatrix.Forward();
                    lightColor0 = light.finalColor;
                }
                else if (dirLightCount == 2)
                {
                    lightDirection1 = -light.localToWorldMatrix.Forward();
                    lightColor1 = light.finalColor;

                    // Only 2 lights supported
                    break;
                }
            }

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky"))
            {
                pass.Initialize(skyMaterial, 3);
                pass.WriteTexture(skyTemp);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_Clouds", clouds);
                pass.ReadTexture("_CloudDepth", cloudDepth);

                lightingSetupResult.SetInputs(pass);
                atmosphereData.SetInputs(pass);
                commonPassData.SetInputs(pass);
                cloudShadowData.SetInputs(pass);
                lookupData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    lightingSetupResult.SetProperties(pass, command);
                    atmosphereData.SetProperties(pass, command);

                    pass.SetFloat(command, "_Samples", settings.RenderSamples);
                    pass.SetConstantBuffer(command, "Exposure", exposureBuffer);
                    pass.SetMatrix(command, "_PixelToWorldViewDir", Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(width, height, jitter, fov, aspect, viewToWorld));

                    pass.SetVector(command, "_LightDirection0", lightDirection0);
                    pass.SetVector(command, "_LightColor0", lightColor0);
                    pass.SetVector(command, "_LightDirection1", lightDirection1);
                    pass.SetVector(command, "_LightColor1", lightColor1);

                    lookupData.SetProperties(pass, command);
                    commonPassData.SetProperties(pass, command);
                    cloudShadowData.SetProperties(pass, command);
                });
            }

            // Reprojection
            var skyColor = textureCache.GetTextures(width, height, camera, true);
            var skyDepth = depthCache.GetTextures(width, height, camera, true);
            var frameCount = frameCountCache.GetTextures(width, height, camera, true);

            var skyTemp2 = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky Temporal"))
            {
                pass.Initialize(skyMaterial, 4);
                pass.WriteTexture(skyTemp2, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(velocity, RenderBufferLoadAction.Load);
                pass.WriteTexture(skyDepth.current, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(frameCount.current, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_SkyInput", skyTemp);
                pass.ReadTexture("_SkyHistory", skyColor.history);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_Clouds", clouds);
                pass.ReadTexture("_CloudDepth", cloudDepth);
                pass.ReadTexture("_PreviousDepth", skyDepth.history);
                pass.ReadTexture("_FrameCount", frameCount.history);
                commonPassData.SetInputs(pass);
                atmosphereData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    pass.SetFloat(command, "_IsFirst", skyColor.wasCreated ? 1.0f : 0.0f);
                    pass.SetFloat(command, "_StationaryBlend", settings.StationaryBlend);
                    pass.SetFloat(command, "_MotionBlend", settings.MotionBlend);
                    pass.SetFloat(command, "_MotionFactor", settings.MotionFactor);
                    pass.SetFloat(command, "_DepthFactor", settings.DepthFactor);
                    pass.SetFloat(command, "_ClampWindow", settings.ClampWindow);

                    pass.SetFloat(command, "_MaxFrameCount", settings.MaxFrameCount);

                    pass.SetInt(command, "_MaxWidth", width - 1);
                    pass.SetInt(command, "_MaxHeight", height - 1);

                    pass.SetMatrix(command, "_PixelToWorldViewDir", Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(width, height, jitter, fov, aspect, viewToWorld));

                    commonPassData.SetProperties(pass, command);
                    atmosphereData.SetProperties(pass, command);
                });
            }

            // Spatial
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky Spatial"))
            {
                pass.Initialize(skyMaterial, 5);
                pass.WriteTexture(target, RenderBufferLoadAction.Load);
                pass.WriteTexture(skyColor.current, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_SkyInput", skyTemp2);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_Clouds", clouds);
                pass.ReadTexture("_CloudDepth", cloudDepth);
                pass.ReadTexture("_SkyDepth", skyDepth.current);
                pass.ReadTexture("_FrameCount", frameCount.current);
                commonPassData.SetInputs(pass);
                atmosphereData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    pass.SetFloat(command, "_BlurSigma", settings.SpatialBlurSigma);
                    pass.SetFloat(command, "_SpatialSamples", settings.SpatialSamples);
                    pass.SetFloat(command, "_SpatialDepthFactor", settings.SpatialDepthFactor);
                    pass.SetFloat(command, "_SpatialBlurFrames", settings.SpatialBlurFrames);
                    pass.SetFloat(command, "_MaxFrameCount", settings.MaxFrameCount);

                    pass.SetInt(command, "_MaxWidth", width - 1);
                    pass.SetInt(command, "_MaxHeight", height - 1);

                    pass.SetMatrix(command, "_PixelToWorldViewDir", Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(width, height, jitter, fov, aspect, viewToWorld));

                    commonPassData.SetProperties(pass, command);
                    atmosphereData.SetProperties(pass, command);
                });
            }
        }

        public void Cleanup()
        {
        }

        public class PassData
        {
        }

        public struct LookupTableResult : IRenderPassData
        {
            private readonly BufferHandle atmospherePropertiesBuffer;
            private readonly RTHandle transmittance;
            private readonly RTHandle multiScatter;
            private readonly RTHandle groundAmbient;
            private readonly RTHandle cdfLookup;
            private readonly RTHandle skyAmbient;
            private readonly RTHandle weightedDepth;

            private Vector4 transmittanceRemap;
            private Vector4 multiScatterRemap;
            private Vector4 skyAmbientRemap;
            private Vector2 groundAmbientRemap;
            private Vector3 cdfLookupSize;

            public LookupTableResult(BufferHandle atmospherePropertiesBuffer, RTHandle transmittance, RTHandle weightedDepth, RTHandle multiScatter, RTHandle groundAmbient, RTHandle cdfLookup, RTHandle skyAmbient, Vector4 transmittanceRemap, Vector4 multiScatterRemap, Vector4 skyAmbientRemap, Vector2 groundAmbientRemap, Vector3 cdfLookupSize)
            {
                this.atmospherePropertiesBuffer = atmospherePropertiesBuffer ?? throw new ArgumentNullException(nameof(atmospherePropertiesBuffer));
                this.transmittance = transmittance ?? throw new ArgumentNullException(nameof(transmittance));
                this.weightedDepth = weightedDepth ?? throw new ArgumentNullException(nameof(weightedDepth));
                this.multiScatter = multiScatter ?? throw new ArgumentNullException(nameof(multiScatter));
                this.groundAmbient = groundAmbient ?? throw new ArgumentNullException(nameof(groundAmbient));
                this.cdfLookup = cdfLookup ?? throw new ArgumentNullException(nameof(cdfLookup));
                this.skyAmbient = skyAmbient ?? throw new ArgumentNullException(nameof(skyAmbient));
                this.transmittanceRemap = transmittanceRemap;
                this.multiScatterRemap = multiScatterRemap;
                this.skyAmbientRemap = skyAmbientRemap;
                this.groundAmbientRemap = groundAmbientRemap;
                this.cdfLookupSize = cdfLookupSize;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadBuffer("AtmosphereProperties", atmospherePropertiesBuffer);
                pass.ReadTexture("_Transmittance", transmittance);
                pass.ReadTexture("_AtmosphereDepth", weightedDepth);
                pass.ReadTexture("_MultiScatter", multiScatter);
                pass.ReadTexture("_SkyAmbient", skyAmbient);
                pass.ReadTexture("_GroundAmbient", groundAmbient);
                pass.ReadTexture("_SkyCdf", cdfLookup);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetVector(command, "_AtmosphereTransmittanceRemap", transmittanceRemap);
                pass.SetVector(command, "_MultiScatterRemap", multiScatterRemap);
                pass.SetVector(command, "_SkyAmbientRemap", skyAmbientRemap);
                pass.SetVector(command, "_GroundAmbientRemap", groundAmbientRemap);
                pass.SetVector(command, "_SkyCdfSize", cdfLookupSize);
            }
        }

        public struct Result : IRenderPassData
        {
            private readonly RTHandle reflectionProbe;
            private readonly BufferHandle ambientBuffer;
            private LookupTableResult lookTableResult;

            public Result(BufferHandle ambientBuffer, RTHandle reflectionProbe, LookupTableResult lookupTableResult)
            {
                this.ambientBuffer = ambientBuffer;
                this.reflectionProbe = reflectionProbe;
                this.lookTableResult = lookupTableResult;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_SkyReflection", reflectionProbe);
                pass.ReadBuffer("AmbientSh", ambientBuffer);
                lookTableResult.SetInputs(pass);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
                lookTableResult.SetProperties(pass, command);
            }
        }
    }

    public struct AtmosphereProperties
    {
        private Vector3 rayleighScatter;
        private readonly float mieScatter;

        private Vector3 ozoneAbsorption;
        private readonly float mieAbsorption;

        private Vector3 groundColor;
        private readonly float miePhase;

        private readonly float rayleighHeight;
        private readonly float mieHeight;
        private readonly float ozoneWidth;
        private readonly float ozoneHeight;

        private readonly float planetRadius;
        private readonly float atmosphereHeight;
        private readonly float topRadius;
        private readonly float padding;

        public AtmosphereProperties(PhysicalSky.Settings settings)
        {
            rayleighScatter = settings.RayleighScatter / settings.EarthScale;
            mieScatter = settings.MieScatter / settings.EarthScale;

            ozoneAbsorption = settings.OzoneAbsorption / settings.EarthScale;
            mieAbsorption = settings.MieAbsorption / settings.EarthScale;

            groundColor = (Vector4)settings.GroundColor.linear;
            miePhase = settings.MiePhase;

            rayleighHeight = settings.RayleighHeight * settings.EarthScale;
            mieHeight = settings.MieHeight * settings.EarthScale;
            ozoneWidth = settings.OzoneWidth * settings.EarthScale;
            ozoneHeight = settings.OzoneHeight * settings.EarthScale;

            planetRadius = settings.PlanetRadius * settings.EarthScale;
            atmosphereHeight = settings.AtmosphereHeight * settings.EarthScale;
            topRadius = (settings.PlanetRadius + settings.AtmosphereHeight) * settings.EarthScale;
            padding = 0f;
        }
    }
}
