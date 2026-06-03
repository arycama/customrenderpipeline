using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static Unmath.Math;

namespace CustomRenderPipeline
{
    public class WaterFft : FrameRenderFeature
    {
        private const int CascadeCount = 4;
        private static readonly IndexedShaderPropertyId smoothnessMapIds = new("SmoothnessOutput");

        private readonly WaterSettings settings;
        private readonly ResourceHandle<GraphicsBuffer> spectrumBuffer, dispersionBuffer;
        private readonly ResourceHandle<RenderTexture> lengthToRoughness;
        private bool roughnessInitialized;
        private ResourceHandle<RenderTexture> displacementCurrent;
        private bool hasHistory;

        private WaterProfile Profile => settings.Profile;

        public WaterFft(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph)
        {
            this.settings = settings;
            spectrumBuffer = renderGraph.GetBuffer(settings.Resolution * settings.Resolution * CascadeCount, sizeof(float) * 4, GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.None, true);
            dispersionBuffer = renderGraph.GetBuffer(settings.Resolution * settings.Resolution * CascadeCount, sizeof(float), GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.None, true);
            lengthToRoughness = renderGraph.GetTexture(new(256, 1), GraphicsFormat.R16_UNorm, isPersistent: true);

            displacementCurrent = renderGraph.GetTexture(settings.Resolution, GraphicsFormat.R16G16B16A16_SFloat, 4, TextureDimension.Tex2DArray, hasMips: true, isPersistent: true);
        }

        protected override void Cleanup(bool disposing)
        {
            renderGraph.ReleasePersistentResource(lengthToRoughness, -1);
            renderGraph.ReleasePersistentResource(spectrumBuffer, -1);
            renderGraph.ReleasePersistentResource(dispersionBuffer, -1);
        }

        public override void Render(ScriptableRenderContext context)
        {
            if (!settings.IsEnabled)
                return;

            // Todo: Should this happen in constructor?
            if (!roughnessInitialized)
            {
                using (var pass = renderGraph.AddComputeRenderPass("Ocean Generate Length to Smoothness"))
                {
                    var roughnessComputeShader = Resources.Load<ComputeShader>("Utility/SmoothnessFilter");

                    var generateLengthToSmoothnessKernel = roughnessComputeShader.FindKernel("GenerateLengthToSmoothness");
                    pass.Initialize(roughnessComputeShader, generateLengthToSmoothnessKernel, 256, 1, 1, false);

                    pass.WriteTexture("_LengthToRoughnessResult", lengthToRoughness);

                    pass.SetRenderFunction(static (command, pass) =>
                    {
                        pass.SetFloat("_MaxIterations", 32);
                        pass.SetFloat("_Resolution", 256);
                    });
                }

                roughnessInitialized = true;
            }

            // Calculate constants
            var patchSizes = new Vector4(Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 0f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 1f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 2f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 3f));
            var timeData = renderGraph.GetResource<TimeData>();

            // Load resources
            var computeShader = Resources.Load<ComputeShader>("OceanFFT");
            var oceanScale = new Vector4(1f / patchSizes.x, 1f / patchSizes.y, 1f / patchSizes.z, 1f / patchSizes.w);

            var maxWaveNumber0 = Sqrt(2 * Square(Pi * settings.Resolution * oceanScale.x));
            var maxWaveNumber1 = Sqrt(2 * Square(Pi * settings.Resolution * oceanScale.y));
            var maxWaveNumber2 = Sqrt(2 * Square(Pi * settings.Resolution * oceanScale.z));
            var maxWaveNumber3 = Sqrt(2 * Square(Pi * settings.Resolution * oceanScale.w));

            var oceanBuffer = renderGraph.SetConstantBuffer(new OceanBufferData
            (
                Profile.WindSpeed,
                Profile.WindAngle,
                Profile.Fetch,
                Profile.SpreadBlend,
                Profile.Swell,
                Profile.PeakEnhancement,
                Profile.ShortWavesFade,
                0f,
                oceanScale,
                new Vector4(0, maxWaveNumber0, maxWaveNumber1, maxWaveNumber2),
                new Vector4(maxWaveNumber0, maxWaveNumber1, maxWaveNumber2, maxWaveNumber3),
                Profile.Gravity,
                (float)timeData.time,
                Profile.TimeScale,
                Profile.SequenceLength
            ));

            // Update spectrum (TODO: Only when properties change)
            using (var pass = renderGraph.AddComputeRenderPass("Ocean Spectrum"))
            {
                pass.Initialize(computeShader, 4, settings.Resolution, settings.Resolution, 4);
                pass.ReadBuffer("OceanData", oceanBuffer);
                pass.WriteBuffer("OceanSpectrumWrite", spectrumBuffer);
                pass.WriteBuffer("OceanDispersionWrite", dispersionBuffer);
            }

            var heightResult = renderGraph.GetTexture(settings.Resolution, GraphicsFormat.R32G32_SFloat, 4, TextureDimension.Tex2DArray);
            var displacementResult = renderGraph.GetTexture(settings.Resolution, GraphicsFormat.R32G32B32A32_SFloat, 4, TextureDimension.Tex2DArray);
            var slopeResult = renderGraph.GetTexture(settings.Resolution, GraphicsFormat.R32G32B32A32_SFloat, 4, TextureDimension.Tex2DArray);

            using (var pass = renderGraph.AddComputeRenderPass("Ocean Fft Row"))
            {
                pass.Initialize(computeShader, 0, 1, settings.Resolution, 4, false);
                pass.WriteTexture("HeightResult", heightResult);
                pass.WriteTexture("DisplacementResult", displacementResult);
                pass.WriteTexture("SlopeResult", slopeResult);
                pass.ReadBuffer("OceanData", oceanBuffer);
                pass.ReadBuffer("OceanSpectrum", spectrumBuffer);
                pass.ReadBuffer("OceanDispersion", dispersionBuffer);
            }

            var normalFoamSmoothness = renderGraph.GetTexture(settings.Resolution, GraphicsFormat.R8G8B8A8_SNorm, 4, TextureDimension.Tex2DArray, hasMips: true);

            ResourceHandle<RenderTexture> displacementHistory;

            using (var pass = renderGraph.AddComputeRenderPass("Ocean Fft Column"))
            {
                // TODO: Why can't this use persistent texture cache
                if (hasHistory)
                {
                    renderGraph.ReleasePersistentResource(displacementCurrent, pass.Index);
                    displacementHistory = displacementCurrent;
                    displacementCurrent = renderGraph.GetTexture(settings.Resolution, GraphicsFormat.R16G16B16A16_SFloat, 4, TextureDimension.Tex2DArray, hasMips: true, isPersistent: true);
                }
                else
                {
                    displacementHistory = displacementCurrent;
                    hasHistory = true;
                }

                pass.Initialize(computeShader, 1, 1, settings.Resolution, 4, false);
                pass.ReadTexture("Height", heightResult);
                pass.ReadTexture("Displacement", displacementResult);
                pass.ReadTexture("Slope", slopeResult);
                pass.WriteTexture("DisplacementOutput", displacementCurrent);
                pass.WriteTexture("OceanNormalFoamSmoothnessWrite", normalFoamSmoothness);
                pass.ReadBuffer("OceanData", oceanBuffer);
            }

            using (var pass = renderGraph.AddComputeRenderPass("Ocean Calculate Normals", new CalculateNormalsData(patchSizes, settings.Resolution, renderGraph.FrameIndex, settings.Material.GetFloat("_Smoothness"), Profile.FoamStrength, Profile.FoamDecay, Profile.FoamThreshold, oceanBuffer)))
            {
                pass.Initialize(computeShader, 2, settings.Resolution, settings.Resolution, 4);
                pass.WriteTexture("DisplacementInput", displacementCurrent);
                pass.WriteTexture("OceanNormalFoamSmoothnessWrite", normalFoamSmoothness);

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    pass.SetVector("_CascadeTexelSizes", data.patchSizes / data.Resolution);
                    pass.SetInt("_OceanTextureSlicePreviousOffset", ((data.FrameIndex & 1) == 0) ? 0 : 4);
                    pass.SetFloat("Smoothness", data.Item4);
                    pass.SetFloat("_FoamStrength", data.FoamStrength);
                    pass.SetFloat("_FoamDecay", data.FoamDecay);
                    pass.SetFloat("_FoamThreshold", data.FoamThreshold);
                    pass.ReadBuffer("OceanData", data.oceanBuffer);
                });
            }

            using (var pass = renderGraph.AddComputeRenderPass("Ocean Generate Filtered Mips", (displacementCurrent, settings.Resolution, settings.Material.GetFloat("_Smoothness"))))
            {
                pass.Initialize(computeShader, 3, (settings.Resolution * 4) >> 2, (settings.Resolution) >> 2, 1);
                pass.ReadTexture("_LengthToRoughness", lengthToRoughness);
                pass.ReadBuffer("OceanData", oceanBuffer);

                var mipCount = (int)Mathf.Log(settings.Resolution, 2) + 1;
                for (var j = 0; j < mipCount; j++)
                {
                    var smoothnessId = smoothnessMapIds.GetProperty(j);
                    pass.WriteTexture(smoothnessId, normalFoamSmoothness, j);
                }

                Assert.IsTrue(renderGraph.RtHandleSystem.GetDescriptor(displacementCurrent).hasMips, "Trying to Generate Mips for a Texture without mips enabled");
                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    // TODO: Do this manually? Since this will be compute shader anyway.. could do in same pass
                    command.GenerateMips(pass.GetRenderTexture(data.displacementCurrent));

                    pass.SetInt("Size", data.Resolution >> 2);
                    pass.SetFloat("Smoothness", data.Item3);
                });
            }

            renderGraph.SetResource(new OceanFftResult(displacementCurrent, displacementHistory, normalFoamSmoothness, lengthToRoughness, oceanBuffer));
        }
    }

    internal struct OceanBufferData
    {
        public float WindSpeed;
        public float WindAngle;
        public float Fetch;
        public float SpreadBlend;
        public float Swell;
        public float PeakEnhancement;
        public float ShortWavesFade;
        public float Item8;
        public Vector4 oceanScale;
        public Vector4 spectrumStart;
        public Vector4 spectrumEnd;
        public float Gravity;
        public float Item13;
        public float TimeScale;
        public float SequenceLength;

        public OceanBufferData(float windSpeed, float windAngle, float fetch, float spreadBlend, float swell, float peakEnhancement, float shortWavesFade, float item8, Vector4 oceanScale, Vector4 spectrumStart, Vector4 spectrumEnd, float gravity, float item13, float timeScale, float sequenceLength)
        {
            WindSpeed = windSpeed;
            WindAngle = windAngle;
            Fetch = fetch;
            SpreadBlend = spreadBlend;
            Swell = swell;
            PeakEnhancement = peakEnhancement;
            ShortWavesFade = shortWavesFade;
            Item8 = item8;
            this.oceanScale = oceanScale;
            this.spectrumStart = spectrumStart;
            this.spectrumEnd = spectrumEnd;
            Gravity = gravity;
            Item13 = item13;
            TimeScale = timeScale;
            SequenceLength = sequenceLength;
        }
    }

    internal struct CalculateNormalsData
    {
        public Vector4 patchSizes;
        public int Resolution;
        public int FrameIndex;
        public float Item4;
        public float FoamStrength;
        public float FoamDecay;
        public float FoamThreshold;
        public ResourceHandle<GraphicsBuffer> oceanBuffer;

        public CalculateNormalsData(Vector4 patchSizes, int resolution, int frameIndex, float item4, float foamStrength, float foamDecay, float foamThreshold, ResourceHandle<GraphicsBuffer> oceanBuffer)
        {
            this.patchSizes = patchSizes;
            Resolution = resolution;
            FrameIndex = frameIndex;
            Item4 = item4;
            FoamStrength = foamStrength;
            FoamDecay = foamDecay;
            FoamThreshold = foamThreshold;
            this.oceanBuffer = oceanBuffer;
        }
    }
}