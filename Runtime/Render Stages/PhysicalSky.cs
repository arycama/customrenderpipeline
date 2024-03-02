using System;
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
            [field: SerializeField] public float AtmosphereThickness { get; private set; } = 100000.0f;
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

            [field: NonSerialized] public int Version { get; private set; }
        }

        private RenderGraph renderGraph;
        private Settings settings;
        private Material skyMaterial;
        private Material ggxConvolutionMaterial;
        private RTHandle transmittance, cdf, multiScatter, groundAmbient, skyAmbient;
        private readonly CameraTextureCache textureCache;
        private int version = -1;

        public PhysicalSky(RenderGraph renderGraph, Settings settings)
        {
            this.renderGraph = renderGraph;
            this.settings = settings;

            skyMaterial = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
            ggxConvolutionMaterial = new Material(Shader.Find("Hidden/Ggx Convolve")) { hideFlags = HideFlags.HideAndDontSave };
            textureCache = new(renderGraph, "Physical Sky");

            transmittance = renderGraph.ImportRenderTexture(new RenderTexture(settings.TransmittanceWidth, settings.TransmittanceHeight, 0, GraphicsFormat.B10G11R11_UFloatPack32));
            cdf = renderGraph.ImportRenderTexture(new RenderTexture(settings.CdfWidth, settings.CdfHeight, 0, GraphicsFormat.R32_SFloat) { dimension = TextureDimension.Tex3D, volumeDepth = settings.CdfDepth });
            multiScatter = renderGraph.ImportRenderTexture(new RenderTexture(settings.MultiScatterWidth, settings.MultiScatterHeight, 0, GraphicsFormat.B10G11R11_UFloatPack32) { enableRandomWrite = true });
            groundAmbient = renderGraph.ImportRenderTexture(new RenderTexture(settings.AmbientGroundWidth, 1, 0, GraphicsFormat.B10G11R11_UFloatPack32) { enableRandomWrite = true });
            skyAmbient = renderGraph.ImportRenderTexture(new RenderTexture(settings.AmbientSkyWidth, settings.AmbientSkyHeight, 0, GraphicsFormat.B10G11R11_UFloatPack32) { enableRandomWrite = true });
        }

        public LookupTableResult GenerateLookupTables()
        {
            var transmittanceRemap = GraphicsUtilities.HalfTexelRemap(settings.TransmittanceWidth, settings.TransmittanceHeight);
            var multiScatterRemap = GraphicsUtilities.HalfTexelRemap(settings.MultiScatterWidth, settings.MultiScatterHeight);
            var groundAmbientRemap = GraphicsUtilities.HalfTexelRemap(settings.AmbientGroundWidth);
            var skyAmbientRemap = GraphicsUtilities.HalfTexelRemap(settings.AmbientSkyWidth, settings.AmbientSkyHeight);

            var result = new LookupTableResult(transmittance, multiScatter, groundAmbient, cdf, skyAmbient, transmittanceRemap, multiScatterRemap, skyAmbientRemap, groundAmbientRemap, new Vector3(settings.CdfWidth, settings.CdfHeight, settings.CdfDepth));

            //if (version >= settings.Version)
            //{
            //    return result;
            //}

            //version = settings.Version;

            // Generate transmittance LUT
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Atmosphere Transmittance"))
            {
                pass.Initialize(skyMaterial, 0);
                pass.WriteTexture(transmittance, RenderBufferLoadAction.DontCare);
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
                    pass.SetVector(command, "_GroundColor", settings.GroundColor.linear);
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
                    pass.SetVector(command, "_GroundColor", settings.GroundColor.linear);
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
                    pass.SetVector(command, "_GroundColor", settings.GroundColor.linear);
                    pass.SetFloat(command, "_Samples", settings.AmbientSkySamples);
                    pass.SetVector(command, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(settings.AmbientSkyWidth, settings.AmbientSkyHeight));
                });
            }

            return result;
        }

        public Result GenerateData(Vector3 viewPosition, LightingSetup.Result lightingSetupResult, BufferHandle exposureBuffer, LookupTableResult passData)
        {
            // Generate Reflection probe
            var skyReflection = renderGraph.GetTexture(settings.ReflectionResolution, settings.ReflectionResolution, GraphicsFormat.B10G11R11_UFloatPack32, dimension: TextureDimension.Cube, hasMips: true, autoGenerateMips: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Sky Reflection"))
            {
                pass.Initialize(skyMaterial, 2);
                pass.WriteTexture(skyReflection, RenderBufferLoadAction.DontCare);
                pass.DepthSlice = RenderTargetIdentifier.AllDepthSlices;

                lightingSetupResult.SetInputs(pass);
                passData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    lightingSetupResult.SetProperties(pass, command);

                    pass.SetFloat(command, "_Samples", settings.ReflectionSamples);
                    pass.SetVector(command, "_ViewPosition", viewPosition);
                    pass.SetConstantBuffer(command, "Exposure", exposureBuffer);

                    var array = ArrayPool<Matrix4x4>.Get(6);

                    for (var i = 0; i < 6; i++)
                    {
                        var rotation = Quaternion.LookRotation(lookAtList[i], upVectorList[i]);
                        var viewToWorld = Matrix4x4.TRS(viewPosition, rotation, Vector3.one);
                        array[i] = Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(settings.ReflectionResolution, settings.ReflectionResolution, Vector2.zero, 90.0f, 1.0f, viewToWorld, true);
                    }

                    pass.SetMatrixArray(command, "_PixelToWorldViewDirs", array);
                    ArrayPool<Matrix4x4>.Release(array);

                    pass.SetVector(command, "_GroundColor", settings.GroundColor.linear);
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
            return new Result(ambientBuffer, reflectionProbe);
        }

        public void Render(RTHandle target, RTHandle depth, BufferHandle exposureBuffer, int width, int height, float fov, float aspect, Matrix4x4 viewToWorld, Vector3 viewPosition, LightingSetup.Result lightingSetupResult, PhysicalSky.Result atmosphereData, Vector2 jitter, IRenderPassData commonPassData, RTHandle clouds, RTHandle cloudDepth, VolumetricClouds.CloudShadowData cloudShadowData, Camera camera)
        {
            var skyTemp = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
            var skyDepth = renderGraph.GetTexture(width, height, GraphicsFormat.R32_SFloat, isScreenTexture: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky"))
            {
                pass.Initialize(skyMaterial, 3);
                pass.WriteTexture(skyTemp);
                pass.WriteTexture(skyDepth);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_Clouds", clouds);
                pass.ReadTexture("_CloudDepth", cloudDepth);

                lightingSetupResult.SetInputs(pass);
                atmosphereData.SetInputs(pass);
                commonPassData.SetInputs(pass);
                cloudShadowData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    lightingSetupResult.SetProperties(pass, command);
                    atmosphereData.SetProperties(pass, command);

                    pass.SetVector(command, "_GroundColor", settings.GroundColor.linear);
                    pass.SetFloat(command, "_Samples", settings.RenderSamples);
                    pass.SetVector(command, "_ViewPosition", viewPosition);
                    pass.SetConstantBuffer(command, "Exposure", exposureBuffer);
                    pass.SetMatrix(command, "_PixelToWorldViewDir", Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(width, height, jitter, fov, aspect, viewToWorld));

                    commonPassData.SetProperties(pass, command);
                    cloudShadowData.SetProperties(pass, command);
                });
            }

            // Reprojection
            var isFirst = textureCache.GetTexture(camera, new RenderTextureDescriptor(width, height, GraphicsFormat.B10G11R11_UFloatPack32, 0), out var current, out var previous);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Temporal"))
            {
                pass.Initialize(skyMaterial, 4);
                pass.WriteTexture(target, RenderBufferLoadAction.Load);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_SkyInput", skyTemp);
                pass.ReadTexture("_SkyHistory", previous);
                pass.ReadTexture("_SkyDepth", skyDepth);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_Clouds", clouds);
                pass.ReadTexture("_CloudDepth", cloudDepth);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    //command.EndSample(sampler);
                    //Debug.Log(sampler.GetRecorder().gpuElapsedNanoseconds / 1000000.0f);

                    pass.SetFloat(command, "_IsFirst", isFirst ? 1.0f : 0.0f);
                    //pass.SetFloat(command, "_StationaryBlend", settings.StationaryBlend);
                    //pass.SetFloat(command, "_MotionBlend", settings.MotionBlend);
                    //pass.SetFloat(command, "_MotionFactor", settings.MotionFactor);

                    pass.SetInt(command, "_MaxWidth", width - 1);
                    pass.SetInt(command, "_MaxHeight", height - 1);

                    pass.SetMatrix(command, "_PixelToWorldViewDir", Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(width, height, jitter, fov, aspect, viewToWorld));
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
            public RTHandle Transmittance { get; }
            public RTHandle MultiScatter { get; }
            public RTHandle GroundAmbient { get; }
            public RTHandle CdfLookup { get; }
            public RTHandle SkyAmbient { get; }

            public Vector4 TransmittanceRemap { get; }
            public Vector4 MultiScatterRemap { get; }
            public Vector4 SkyAmbientRemap { get; }
            public Vector2 GroundAmbientRemap { get; }
            public Vector3 CdfLookupSize { get; }

            public LookupTableResult(RTHandle transmittance, RTHandle multiScatter, RTHandle groundAmbient, RTHandle cdfLookup, RTHandle skyAmbient, Vector4 transmittanceRemap, Vector4 multiScatterRemap, Vector4 skyAmbientRemap, Vector2 groundAmbientRemap, Vector3 cdfLookupSize)
            {
                Transmittance = transmittance ?? throw new ArgumentNullException(nameof(transmittance));
                MultiScatter = multiScatter ?? throw new ArgumentNullException(nameof(multiScatter));
                GroundAmbient = groundAmbient ?? throw new ArgumentNullException(nameof(groundAmbient));
                CdfLookup = cdfLookup ?? throw new ArgumentNullException(nameof(cdfLookup));
                SkyAmbient = skyAmbient ?? throw new ArgumentNullException(nameof(skyAmbient));
                TransmittanceRemap = transmittanceRemap;
                MultiScatterRemap = multiScatterRemap;
                SkyAmbientRemap = skyAmbientRemap;
                GroundAmbientRemap = groundAmbientRemap;
                CdfLookupSize = cdfLookupSize;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_Transmittance", Transmittance);
                pass.ReadTexture("_MultiScatter", MultiScatter);
                pass.ReadTexture("_SkyAmbient", SkyAmbient);
                pass.ReadTexture("_GroundAmbient", GroundAmbient);
                pass.ReadTexture("_SkyCdf", CdfLookup);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetVector(command, "_AtmosphereTransmittanceRemap", TransmittanceRemap);
                pass.SetVector(command, "_MultiScatterRemap", MultiScatterRemap);
                pass.SetVector(command, "_SkyAmbientRemap", SkyAmbientRemap);
                pass.SetVector(command, "_GroundAmbientRemap", GroundAmbientRemap);
                pass.SetVector(command, "_SkyCdfSize", CdfLookupSize);
            }
        }

        public struct Result : IRenderPassData
        {
            public RTHandle ReflectionProbe { get; }
            public BufferHandle AmbientBuffer { get; }

            public Result(BufferHandle ambientBuffer, RTHandle reflectionProbe)
            {
                AmbientBuffer = ambientBuffer;
                ReflectionProbe = reflectionProbe;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_SkyReflection", ReflectionProbe);
                pass.ReadBuffer("AmbientSh", AmbientBuffer);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
            }
        }
    }
}
