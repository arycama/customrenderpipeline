using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Arycama.CustomRenderPipeline
{
    public class PhysicalSkyGenerateData : RenderFeature
    {
        private readonly PhysicalSky.Settings settings;
        private readonly VolumetricClouds.Settings cloudSettings;
        private readonly Material skyMaterial, ggxConvolutionMaterial;

        public PhysicalSkyGenerateData(PhysicalSky.Settings settings, VolumetricClouds.Settings cloudSettings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            this.cloudSettings = cloudSettings;
            skyMaterial = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
            ggxConvolutionMaterial = new Material(Shader.Find("Hidden/GgxConvolve")) { hideFlags = HideFlags.HideAndDontSave };
        }

        protected override void Cleanup(bool disposing)
        {
            Object.DestroyImmediate(ggxConvolutionMaterial);
        }

        public override void Render()
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
                    pass.SetFloat("_Samples", settings.TransmittanceSamples);
                    var scaleOffset = GraphicsUtilities.HalfTexelRemap(settings.TransmittanceWidth, settings.TransmittanceHeight);
                    pass.SetVector("_ScaleOffset", scaleOffset);
                    pass.SetFloat("_TransmittanceWidth", settings.TransmittanceWidth);
                    pass.SetFloat("_TransmittanceHeight", settings.TransmittanceHeight);
                });
            }

            renderGraph.SetResource(new SkyTransmittanceData(skyTransmittance, settings.TransmittanceWidth, settings.TransmittanceHeight)); ;

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
                    pass.SetFloat("_Samples", settings.LuminanceSamples);
                    var scaleOffset = GraphicsUtilities.HalfTexelRemap(settings.LuminanceWidth, settings.LuminanceHeight);
                    pass.SetVector("_ScaleOffset", scaleOffset);
                    pass.SetFloat("_TransmittanceWidth", settings.TransmittanceWidth);
                    pass.SetFloat("_TransmittanceHeight", settings.TransmittanceHeight);
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
                    pass.SetFloat("_Samples", settings.CdfSamples);
                    pass.SetFloat("_ColorChannelScale", (settings.CdfWidth - 1.0f) / (settings.CdfWidth / 3.0f));
                    pass.SetVector("_SkyCdfSize", new Vector2(settings.CdfWidth, settings.CdfHeight));
                    pass.SetVector("_CdfSize", new Vector2(settings.CdfWidth, settings.CdfHeight));
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

                    pass.SetFloat("_Samples", settings.TransmittanceSamples);
                    pass.SetVector("_ScaleOffset", GraphicsUtilities.RemapHalfTexelTo01(settings.TransmittanceWidth, settings.TransmittanceHeight));
                });
            }

            var viewData = renderGraph.GetResource<ViewData>();
            var keyword = string.Empty;
            var viewHeight = viewData.ViewPosition.y;
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
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<CloudData>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<DirectionalLightInfo>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<SkyTransmittanceData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    cloudSettings.SetCloudPassData(pass);
                    pass.SetFloat("_Samples", settings.ReflectionSamples);

                    var array = ArrayPool<Matrix4x4>.Get(6);

                    for (var i = 0; i < 6; i++)
                    {
                        var rotation = Quaternion.LookRotation(GraphicsUtilities.lookAtList[i], GraphicsUtilities.upVectorList[i]);
                        var viewToWorld = Matrix4x4.TRS(viewData.ViewPosition, rotation, Vector3.one);
                        array[i] = Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(settings.ReflectionResolution, settings.ReflectionResolution, Vector2.zero, 90.0f, 1.0f, viewToWorld, true);
                    }

                    pass.SetMatrixArray("_PixelToWorldViewDirs", array);
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
                    pass.SetFloat("_MipLevel", mipLevel);
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
                        pass.SetFloat("_Samples", settings.ConvolutionSamples);

                        var array = ArrayPool<Matrix4x4>.Get(6);

                        for (var j = 0; j < 6; j++)
                        {
                            var resolution = settings.ReflectionResolution >> index;
                            var rotation = Quaternion.LookRotation(GraphicsUtilities.lookAtList[j], GraphicsUtilities.upVectorList[j]);
                            var viewToWorld = Matrix4x4.TRS(viewData.ViewPosition, rotation, Vector3.one);
                            array[j] = Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(resolution, resolution, Vector2.zero, 90.0f, 1.0f, viewToWorld, true);
                        }

                        pass.SetMatrixArray("_PixelToWorldViewDirs", array);
                        ArrayPool<Matrix4x4>.Release(array);

                        pass.SetFloat("_Level", index);
                        pass.SetFloat("_InvOmegaP", invOmegaP);

                        var perceptualRoughness = Mathf.Clamp01(index / (float)mipLevels);
                        var mipPerceptualRoughness = Mathf.Clamp01(1.7f / 1.4f - Mathf.Sqrt(2.89f / 1.96f - (2.8f / 1.96f) * perceptualRoughness));
                        var mipRoughness = mipPerceptualRoughness * mipPerceptualRoughness;
                        pass.SetFloat("_Roughness", mipRoughness);
                    });
                }
            }

            // Specular convolution
            renderGraph.SetResource(new SkyReflectionAmbientData(ambientBuffer, reflectionProbe, cdf, skyLuminance, weightedDepth, new Vector2(settings.LuminanceWidth, settings.LuminanceHeight), new Vector2(settings.CdfWidth, settings.CdfHeight))); ;
        }
    }
}
