using Arycama.CustomRenderPipeline;
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
        public static readonly Vector3[] lookAtList =
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
        public static readonly Vector3[] upVectorList =
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
            [field: SerializeField] public float CloudScatter { get; private set; } = 3.996e-6f;
            [field: SerializeField] public float PlanetRadius { get; private set; } = 6360000.0f;
            [field: SerializeField] public float AtmosphereHeight { get; private set; } = 100000.0f;
            [field: SerializeField] public Color GroundColor { get; private set; } = Color.grey;

            [field: Header("Transmittance Lookup")]
            [field: SerializeField] public int TransmittanceWidth { get; private set; } = 128;
            [field: SerializeField] public int TransmittanceHeight { get; private set; } = 64;
            [field: SerializeField] public int TransmittanceSamples { get; private set; } = 64;

            [field: Header("Luminance Lookup")]
            [field: SerializeField] public int LuminanceWidth { get; private set; } = 128;
            [field: SerializeField] public int LuminanceHeight { get; private set; } = 64;
            [field: SerializeField] public int LuminanceSamples { get; private set; } = 64;

            [field: Header("CDF Lookup")]
            [field: SerializeField] public int CdfWidth { get; private set; } = 64;
            [field: SerializeField] public int CdfHeight { get; private set; } = 64;
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
            [field: SerializeField, Range(0.0f, 2.0f)] public float DepthFactor { get; private set; } = 0.0f;
            [field: SerializeField, Range(0.0f, 2.0f)] public float ClampWindow { get; private set; } = 1.0f;

            [field: Header("Spatial")]
            [field: SerializeField, Range(0, 32)] public int SpatialSamples { get; private set; } = 4;
            [field: SerializeField, Min(0.0f)] public float SpatialDepthFactor { get; private set; } = 0.1f;
            [field: SerializeField, Min(0.0f)] public float SpatialBlurSigma { get; private set; } = 0.1f;
            [field: SerializeField, Range(0, 32)] public int SpatialBlurFrames { get; private set; } = 8;
            [field: SerializeField] public Texture2D miePhase;
            [field: NonSerialized] public int Version { get; private set; }
        }

        private readonly RenderGraph renderGraph;
        private readonly Settings settings;
        private readonly VolumetricClouds.Settings cloudSettings;
        private readonly Material skyMaterial;
        private readonly Material ggxConvolutionMaterial;
        private readonly RTHandle transmittance, multiScatter, groundAmbient, skyAmbient;
        private int version = -1;

        private readonly PersistentRTHandleCache textureCache;

        public PhysicalSky(RenderGraph renderGraph, Settings settings, VolumetricClouds.Settings cloudSettings)
        {
            this.renderGraph = renderGraph;
            this.settings = settings;
            this.cloudSettings = cloudSettings;

            skyMaterial = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
            ggxConvolutionMaterial = new Material(Shader.Find("Hidden/GgxConvolve")) { hideFlags = HideFlags.HideAndDontSave };
            textureCache = new(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Physical Sky");

            transmittance = renderGraph.GetTexture(settings.TransmittanceWidth, settings.TransmittanceHeight, GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
            multiScatter = renderGraph.GetTexture(settings.MultiScatterWidth, settings.MultiScatterHeight, GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
            groundAmbient = renderGraph.GetTexture(settings.AmbientGroundWidth, 1, GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
            skyAmbient = renderGraph.GetTexture(settings.AmbientSkyWidth, settings.AmbientSkyHeight, GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
        }

        public void GenerateLookupTables()
        {
            var atmospherePropertiesBuffer = renderGraph.SetConstantBuffer((
                    rayleighScatter: settings.RayleighScatter / settings.EarthScale,
                    mieScatter: settings.MieScatter / settings.EarthScale,
                    ozoneAbsorption: settings.OzoneAbsorption / settings.EarthScale,
                    mieAbsorption: settings.MieAbsorption / settings.EarthScale,
                    groundColor: settings.GroundColor.linear.AsVector3(),
                    miePhase: settings.MiePhase,
                    rayleighHeight: settings.RayleighHeight * settings.EarthScale,
                    mieHeight: settings.MieHeight * settings.EarthScale,
                    ozoneWidth: settings.OzoneWidth * settings.EarthScale,
                    ozoneHeight: settings.OzoneHeight * settings.EarthScale,
                    planetRadius: settings.PlanetRadius * settings.EarthScale,
                    atmosphereHeight: settings.AtmosphereHeight * settings.EarthScale,
                    topRadius: (settings.PlanetRadius + settings.AtmosphereHeight) * settings.EarthScale,
                    cloudScatter: settings.CloudScatter
            ));

            var transmittanceRemap = GraphicsUtilities.HalfTexelRemap(settings.TransmittanceWidth, settings.TransmittanceHeight);
            var multiScatterRemap = GraphicsUtilities.HalfTexelRemap(settings.MultiScatterWidth, settings.MultiScatterHeight);
            var groundAmbientRemap = GraphicsUtilities.HalfTexelRemap(settings.AmbientGroundWidth);
            var skyAmbientRemap = GraphicsUtilities.HalfTexelRemap(settings.AmbientSkyWidth, settings.AmbientSkyHeight);

            var result = new AtmospherePropertiesAndTables(atmospherePropertiesBuffer, transmittance, multiScatter, groundAmbient, skyAmbient, transmittanceRemap, multiScatterRemap, skyAmbientRemap, groundAmbientRemap, new Vector3(settings.TransmittanceWidth, settings.TransmittanceHeight));

            if (version >= settings.Version)
                return;

            version = settings.Version;

            // Generate transmittance LUT
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Atmosphere Transmittance"))
            {
                pass.Initialize(skyMaterial, 0);
                pass.WriteTexture(transmittance, RenderBufferLoadAction.DontCare);
                result.SetInputs(pass);

                pass.SetRenderFunction((command, pass) =>
                {
                    command.SetGlobalTexture("_MiePhaseTexture", settings.miePhase);

                    result.SetProperties(pass, command);
                    pass.SetFloat(command, "_Samples", settings.TransmittanceSamples);
                    pass.SetVector(command, "_ScaleOffset", GraphicsUtilities.RemapHalfTexelTo01(settings.TransmittanceWidth, settings.TransmittanceHeight));
                    pass.SetFloat(command, "_TransmittanceWidth", settings.TransmittanceWidth);
                    pass.SetFloat(command, "_TransmittanceHeight", settings.TransmittanceHeight);

                });
            }

            // Generate multi-scatter LUT
            var computeShader = Resources.Load<ComputeShader>("PhysicalSky");
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Atmosphere Multi Scatter"))
            {
                pass.Initialize(computeShader, 0, settings.MultiScatterWidth, settings.MultiScatterHeight, 1, false);
                pass.WriteTexture("_MultiScatterResult", multiScatter);
                result.SetInputs(pass);

                pass.SetRenderFunction((command, pass) =>
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

                pass.SetRenderFunction((command, pass) =>
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

                pass.SetRenderFunction((command, pass) =>
                {
                    result.SetProperties(pass, command);
                    pass.SetFloat(command, "_Samples", settings.AmbientSkySamples);
                    pass.SetVector(command, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(settings.AmbientSkyWidth, settings.AmbientSkyHeight));
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(result, renderGraph.FrameIndex, true);
        }

        public void GenerateData(Vector3 viewPosition, CullingResults cullingResults, Vector3 cameraPosition)
        {
            // Sky transmittance
            var skyTransmittance = renderGraph.GetTexture(settings.TransmittanceWidth, settings.TransmittanceHeight, GraphicsFormat.B10G11R11_UFloatPack32, 2, TextureDimension.Tex2DArray);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Sky Transmittance"))
            {
                pass.Initialize(skyMaterial, skyMaterial.FindPass("Transmittance Lookup 2"));
                pass.WriteTexture(skyTransmittance, RenderBufferLoadAction.DontCare);
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<DirectionalLightInfo>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat(command, "_Samples", settings.TransmittanceSamples);
                    var scaleOffset = GraphicsUtilities.HalfTexelRemap(settings.TransmittanceWidth, settings.TransmittanceHeight);
                    pass.SetVector(command, "_ScaleOffset", scaleOffset);
                    pass.SetFloat(command, "_TransmittanceWidth", settings.TransmittanceWidth);
                    pass.SetFloat(command, "_TransmittanceHeight", settings.TransmittanceHeight);
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new SkyTransmittanceData(skyTransmittance, settings.TransmittanceWidth, settings.TransmittanceHeight), renderGraph.FrameIndex);

            // Sky luminance
            var skyLuminance = renderGraph.GetTexture(settings.LuminanceWidth, settings.LuminanceHeight, GraphicsFormat.B10G11R11_UFloatPack32, 2, TextureDimension.Tex2DArray);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Sky Luminance"))
            {
                pass.Initialize(skyMaterial, skyMaterial.FindPass("Luminance LUT"));
                pass.WriteTexture(skyLuminance, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_SkyTransmittance", skyTransmittance);
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<DirectionalLightInfo>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<SkyTransmittanceData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat(command, "_Samples", settings.LuminanceSamples);
                    var scaleOffset = GraphicsUtilities.HalfTexelRemap(settings.LuminanceWidth, settings.LuminanceHeight);
                    pass.SetVector(command, "_ScaleOffset", scaleOffset);
                    pass.SetFloat(command, "_TransmittanceWidth", settings.TransmittanceWidth);
                    pass.SetFloat(command, "_TransmittanceHeight", settings.TransmittanceHeight);
                });
            }

            var cdf = renderGraph.GetTexture(settings.CdfWidth, settings.CdfHeight, GraphicsFormat.R32_SFloat, dimension: TextureDimension.Tex2DArray, volumeDepth: 6);

            // CDF
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Atmosphere CDF"))
            {
                pass.Initialize(skyMaterial, 1);
                pass.WriteTexture(cdf, RenderBufferLoadAction.DontCare);
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<DirectionalLightInfo>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<SkyTransmittanceData>();
                pass.ReadTexture("SkyLuminance", skyLuminance);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat(command, "_Samples", settings.CdfSamples);
                    pass.SetFloat(command, "_ColorChannelScale", (settings.CdfWidth - 1.0f) / (settings.CdfWidth / 3.0f));
                    pass.SetVector(command, "_SkyCdfSize", new Vector2(settings.CdfWidth, settings.CdfHeight));
                    pass.SetVector(command, "_CdfSize", new Vector2(settings.CdfWidth, settings.CdfHeight));
                });
            }

            var weightedDepth = renderGraph.GetTexture(settings.TransmittanceWidth, settings.TransmittanceHeight, GraphicsFormat.R32_SFloat);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Atmosphere Transmittance"))
            {
                pass.Initialize(skyMaterial, skyMaterial.FindPass("Transmittance Depth Lookup"));
                pass.WriteTexture(weightedDepth, RenderBufferLoadAction.DontCare);

                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<SkyTransmittanceData>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    command.SetGlobalTexture("_MiePhaseTexture", settings.miePhase);

                    pass.SetFloat(command, "_Samples", settings.TransmittanceSamples);
                    pass.SetVector(command, "_ScaleOffset", GraphicsUtilities.RemapHalfTexelTo01(settings.TransmittanceWidth, settings.TransmittanceHeight));
                });
            }

            var keyword = string.Empty;
            var viewHeight = cameraPosition.y;
            if (viewHeight > cloudSettings.StartHeight)
            {
                if (viewHeight > cloudSettings.StartHeight + cloudSettings.LayerThickness)
                {
                    keyword = "ABOVE_CLOUD_LAYER";
                }
            }
            else
            {
                keyword = "BELOW_CLOUD_LAYER";
            }

            // Generate Reflection probe
            var skyReflection = renderGraph.GetTexture(settings.ReflectionResolution, settings.ReflectionResolution, GraphicsFormat.B10G11R11_UFloatPack32, dimension: TextureDimension.Cube, hasMips: true, autoGenerateMips: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Sky Reflection"))
            {
                pass.Initialize(skyMaterial, 2, 1, keyword);
                pass.WriteTexture(skyReflection, RenderBufferLoadAction.DontCare);

                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<VolumetricClouds.CloudData>();
                pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<DirectionalLightInfo>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<SkyTransmittanceData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    cloudSettings.SetCloudPassData(command, pass);
                    pass.SetFloat(command, "_Samples", settings.ReflectionSamples);

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

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat(command, "_MipLevel", mipLevel);
                });
            }

            // Copy ambient
            var ambientBuffer = renderGraph.GetBuffer(7, sizeof(float) * 4, GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination);
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Atmosphere Ambient Probe Copy"))
            {
                pass.SetRenderFunction((command, pass) =>
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

                pass.SetRenderFunction((command, pass) =>
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
                    pass.MipLevel = i;
                    var index = i;

                    pass.SetRenderFunction((command, pass) =>
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
            renderGraph.ResourceMap.SetRenderPassData(new ReflectionAmbientData(ambientBuffer, reflectionProbe, cdf, skyLuminance, weightedDepth, new Vector2(settings.LuminanceWidth, settings.LuminanceHeight), new Vector2(settings.CdfWidth, settings.CdfHeight)), renderGraph.FrameIndex);
        }

        public void Render(RTHandle depth, int width, int height, float fov, float aspect, Matrix4x4 viewToWorld, Vector2 jitter, Camera camera, CullingResults cullingResults, RTHandle velocity)
        {
            var skyTemp = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky"))
            {
                pass.Initialize(skyMaterial, 3);
                pass.WriteTexture(skyTemp, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_Depth", depth);

                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<VolumetricClouds.CloudRenderResult>();
                pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<ReflectionAmbientData>();
                pass.AddRenderPassData<DirectionalLightInfo>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<SkyTransmittanceData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat(command, "_Samples", settings.RenderSamples);
                });
            }

            // Spatial
            var skyTemp2 = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky Spatial"))
            {
                pass.Initialize(skyMaterial, 5);
                pass.WriteTexture(skyTemp2, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_SkyInput", skyTemp);
                pass.ReadTexture("_Depth", depth);
                pass.AddRenderPassData<VolumetricClouds.CloudRenderResult>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat(command, "_BlurSigma", settings.SpatialBlurSigma);
                    pass.SetFloat(command, "_SpatialSamples", settings.SpatialSamples);
                    pass.SetFloat(command, "_SpatialDepthFactor", settings.SpatialDepthFactor);
                    pass.SetFloat(command, "_SpatialBlurFrames", settings.SpatialBlurFrames);
                    pass.SetFloat(command, "_MaxFrameCount", settings.MaxFrameCount);

                    pass.SetInt(command, "_MaxWidth", width - 1);
                    pass.SetInt(command, "_MaxHeight", height - 1);
                });
            }

            // Reprojection
            var skyColor = textureCache.GetTextures(width, height, camera, true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky Temporal"))
            {
                pass.Initialize(skyMaterial, 4);
                pass.WriteTexture(skyColor.current, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_SkyInput", skyTemp2);
                pass.ReadTexture("_SkyHistory", skyColor.history);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("Velocity", velocity);
                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<TemporalAA.TemporalAAData>();
                pass.AddRenderPassData<VolumetricClouds.CloudRenderResult>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<PreviousFrameDepth>();
                pass.AddRenderPassData<PreviousFrameVelocity>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetVector(command, "_SkyHistoryScaleLimit", skyColor.history.ScaleLimit2D);

                    pass.SetFloat(command, "_IsFirst", skyColor.wasCreated ? 1.0f : 0.0f);
                    pass.SetFloat(command, "_StationaryBlend", settings.StationaryBlend);
                    pass.SetFloat(command, "_MotionBlend", settings.MotionBlend);
                    pass.SetFloat(command, "_MotionFactor", settings.MotionFactor);
                    pass.SetFloat(command, "_DepthFactor", settings.DepthFactor);
                    pass.SetFloat(command, "_ClampWindow", settings.ClampWindow);

                    pass.SetFloat(command, "_MaxFrameCount", settings.MaxFrameCount);

                    pass.SetInt(command, "_MaxWidth", width - 1);
                    pass.SetInt(command, "_MaxHeight", height - 1);
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new SkyResultData(skyColor.current), renderGraph.FrameIndex);
        }

        public void Cleanup()
        {
        }

        public struct AtmospherePropertiesAndTables : IRenderPassData
        {
            private readonly BufferHandle atmospherePropertiesBuffer;
            private readonly RTHandle transmittance;
            private readonly RTHandle multiScatter;
            private readonly RTHandle groundAmbient;
            private readonly RTHandle skyAmbient;

            private Vector4 transmittanceRemap;
            private Vector4 multiScatterRemap;
            private Vector4 skyAmbientRemap;
            private Vector2 groundAmbientRemap;
            private Vector2 transmittanceSize;

            public AtmospherePropertiesAndTables(BufferHandle atmospherePropertiesBuffer, RTHandle transmittance, RTHandle multiScatter, RTHandle groundAmbient, RTHandle skyAmbient, Vector4 transmittanceRemap, Vector4 multiScatterRemap, Vector4 skyAmbientRemap, Vector2 groundAmbientRemap, Vector2 transmittanceSize)
            {
                this.atmospherePropertiesBuffer = atmospherePropertiesBuffer ?? throw new ArgumentNullException(nameof(atmospherePropertiesBuffer));
                this.transmittance = transmittance ?? throw new ArgumentNullException(nameof(transmittance));
                this.multiScatter = multiScatter ?? throw new ArgumentNullException(nameof(multiScatter));
                this.groundAmbient = groundAmbient ?? throw new ArgumentNullException(nameof(groundAmbient));
                this.skyAmbient = skyAmbient ?? throw new ArgumentNullException(nameof(skyAmbient));
                this.transmittanceRemap = transmittanceRemap;
                this.multiScatterRemap = multiScatterRemap;
                this.skyAmbientRemap = skyAmbientRemap;
                this.groundAmbientRemap = groundAmbientRemap;
                this.transmittanceSize = transmittanceSize;
            }

            public readonly void SetInputs(RenderPass pass)
            {
                pass.ReadBuffer("AtmosphereProperties", atmospherePropertiesBuffer);
                pass.ReadTexture("_Transmittance", transmittance);
                pass.ReadTexture("_MultiScatter", multiScatter);
                pass.ReadTexture("_SkyAmbient", skyAmbient);
                pass.ReadTexture("_GroundAmbient", groundAmbient);
            }

            public readonly void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetVector(command, "_AtmosphereTransmittanceRemap", transmittanceRemap);
                pass.SetVector(command, "_MultiScatterRemap", multiScatterRemap);
                pass.SetVector(command, "_SkyAmbientRemap", skyAmbientRemap);
                pass.SetVector(command, "_GroundAmbientRemap", groundAmbientRemap);
                pass.SetVector(command, "_TransmittanceSize", transmittanceSize);
            }
        }

        public struct ReflectionAmbientData : IRenderPassData
        {
            private readonly RTHandle reflectionProbe, skyCdf;
            private readonly BufferHandle ambientBuffer;
            private readonly RTHandle skyLuminance;
            private readonly RTHandle weightedDepth;
            private Vector2 skyLuminanceSize;
            private Vector2 cdfLookupSize;

            public ReflectionAmbientData(BufferHandle ambientBuffer, RTHandle reflectionProbe, RTHandle skyCdf, RTHandle skyLuminance, RTHandle weightedDepth, Vector2 skyLuminanceSize, Vector2 cdfLookupSize)
            {
                this.ambientBuffer = ambientBuffer;
                this.reflectionProbe = reflectionProbe;
                this.skyCdf = skyCdf;
                this.skyLuminance = skyLuminance;
                this.weightedDepth = weightedDepth;
                this.skyLuminanceSize = skyLuminanceSize;
                this.cdfLookupSize = cdfLookupSize;
            }

            public readonly void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_SkyReflection", reflectionProbe);
                pass.ReadTexture("_SkyCdf", skyCdf);
                pass.ReadBuffer("AmbientSh", ambientBuffer);
                pass.ReadTexture("SkyLuminance", skyLuminance);
                pass.ReadTexture("_SkyCdf", skyCdf);
                pass.ReadTexture("_AtmosphereDepth", weightedDepth);
            }

            public readonly void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetVector(command, "_SkyCdfSize", new Vector2(skyCdf.Width, skyCdf.Height));
                pass.SetVector(command, "SkyLuminanceScaleLimit", skyLuminance.ScaleLimit2D);
                pass.SetVector(command, "SkyLuminanceSize", skyLuminanceSize);
                pass.SetVector(command, "_SkyCdfSize", cdfLookupSize);
            }
        }
    }
}

public readonly struct SkyResultData : IRenderPassData
{
    public RTHandle SkyTexture { get; }

    public SkyResultData(RTHandle skyTexture)
    {
        SkyTexture = skyTexture ?? throw new ArgumentNullException(nameof(skyTexture));
    }

    public readonly void SetInputs(RenderPass pass)
    {
        pass.ReadTexture("SkyTexture", SkyTexture);
    }

    public readonly void SetProperties(RenderPass pass, CommandBuffer command)
    {
        pass.SetVector(command, "SkyTextureScaleLimit", SkyTexture.ScaleLimit2D);
    }
}

public readonly struct SkyTransmittanceData : IRenderPassData
{
    private readonly RTHandle skyTransmittance;
    private readonly int width, height;

    public SkyTransmittanceData(RTHandle skyTransmittance, int width, int height)
    {
        this.skyTransmittance = skyTransmittance ?? throw new ArgumentNullException(nameof(skyTransmittance));
        this.width = width;
        this.height = height;
    }

    public void SetInputs(RenderPass pass)
    {
        pass.ReadTexture("_SkyTransmittance", skyTransmittance);
    }

    public void SetProperties(RenderPass pass, CommandBuffer command)
    {
        pass.SetFloat(command, "_TransmittanceWidth", width);
        pass.SetFloat(command, "_TransmittanceHeight", height);
    }
}