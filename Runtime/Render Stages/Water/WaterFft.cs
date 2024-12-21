using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public class WaterFft : RenderFeature
    {
        private const int CascadeCount = 4;
        private static readonly IndexedShaderPropertyId smoothnessMapIds = new("SmoothnessOutput");

        private readonly WaterSettings settings;
        private readonly GraphicsBuffer spectrumBuffer, dispersionBuffer;
        private readonly RTHandle lengthToRoughness;
        private readonly bool disposedValue;
        private bool roughnessInitialized;
        private RTHandle displacementCurrent;

        private WaterProfile Profile => settings.Profile;

        public WaterFft(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph)
        {
            this.settings = settings;
            spectrumBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, settings.Resolution * settings.Resolution * CascadeCount, sizeof(float) * 4) { name = "Ocean Spectrum" };
            dispersionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, settings.Resolution * settings.Resolution * CascadeCount, sizeof(float)) { name = "Ocean Spectrum" };
            lengthToRoughness = renderGraph.GetTexture(256, 1, GraphicsFormat.R16_UNorm, isPersistent: true);
        }

        protected override void Cleanup(bool disposing)
        {
            lengthToRoughness.IsPersistent = false;
            spectrumBuffer.Dispose();
            dispersionBuffer.Dispose();
        }

        public override void Render()
        {
            // Todo: Should this happen in constructor?
            if (!roughnessInitialized)
            {
                using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Generate Length to Smoothness"))
                {
                    var roughnessComputeShader = Resources.Load<ComputeShader>("Utility/SmoothnessFilter");

                    var generateLengthToSmoothnessKernel = roughnessComputeShader.FindKernel("GenerateLengthToSmoothness");
                    pass.Initialize(roughnessComputeShader, generateLengthToSmoothnessKernel, 256, 1, 1, false);

                    pass.WriteTexture("_LengthToRoughnessResult", lengthToRoughness);

                    pass.SetRenderFunction((command, pass) =>
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
            var oceanBuffer = renderGraph.SetConstantBuffer((
                Profile.WindSpeed,
                Profile.WindAngle,
                Profile.Fetch,
                Profile.SpreadBlend,
                Profile.Swell,
                Profile.PeakEnhancement,
                Profile.ShortWavesFade,
                0f,
                oceanScale: new Vector4(1f / patchSizes.x, 1f / patchSizes.y, 1f / patchSizes.z, 1f / patchSizes.w),
                spectrumStart: new Vector4(0, Profile.MaxWaveNumber * patchSizes.y / patchSizes.x, Profile.MaxWaveNumber * patchSizes.z / patchSizes.y, Profile.MaxWaveNumber * patchSizes.w / patchSizes.z),
                spectrumEnd: new Vector4(Profile.MaxWaveNumber, Profile.MaxWaveNumber, Profile.MaxWaveNumber, settings.Resolution),
                Profile.Gravity,
                (float)timeData.Time,
                Profile.TimeScale,
                Profile.SequenceLength
            ));

            // Update spectrum (TODO: Only when properties change)
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Spectrum"))
            {
                pass.Initialize(computeShader, 4, settings.Resolution, settings.Resolution, 4);
                pass.ReadBuffer("OceanData", oceanBuffer);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetBuffer("OceanSpectrumWrite", spectrumBuffer);
                    pass.SetBuffer("OceanDispersionWrite", dispersionBuffer);
                });
            }

            var heightResult = renderGraph.GetTexture(settings.Resolution, settings.Resolution, GraphicsFormat.R32G32_SFloat, 4, TextureDimension.Tex2DArray);
            var displacementResult = renderGraph.GetTexture(settings.Resolution, settings.Resolution, GraphicsFormat.R32G32B32A32_SFloat, 4, TextureDimension.Tex2DArray);
            var slopeResult = renderGraph.GetTexture(settings.Resolution, settings.Resolution, GraphicsFormat.R32G32B32A32_SFloat, 4, TextureDimension.Tex2DArray);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Fft Row"))
            {
                pass.Initialize(computeShader, 0, 1, settings.Resolution, 4, false);
                pass.WriteTexture("HeightResult", heightResult);
                pass.WriteTexture("DisplacementResult", displacementResult);
                pass.WriteTexture("SlopeResult", slopeResult);
                pass.ReadBuffer("OceanData", oceanBuffer);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetBuffer("OceanSpectrum", spectrumBuffer);
                    pass.SetBuffer("OceanDispersion", dispersionBuffer);
                });
            }

            // TODO: Why can't this use persistent texture cache
            var displacementHistory = displacementCurrent;
            var hasDisplacementHistory = displacementHistory != null;
            displacementCurrent = renderGraph.GetTexture(settings.Resolution, settings.Resolution, GraphicsFormat.R16G16B16A16_SFloat, 4, TextureDimension.Tex2DArray, hasMips: true, isPersistent: true);
            if (hasDisplacementHistory)
                displacementHistory.IsPersistent = false;
            else
                displacementHistory = displacementCurrent;

            var normalFoamSmoothness = renderGraph.GetTexture(settings.Resolution, settings.Resolution, GraphicsFormat.R8G8B8A8_SNorm, 4, TextureDimension.Tex2DArray, hasMips: true);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Fft Column"))
            {
                pass.Initialize(computeShader, 1, 1, settings.Resolution, 4, false);
                pass.ReadTexture("Height", heightResult);
                pass.ReadTexture("Displacement", displacementResult);
                pass.ReadTexture("Slope", slopeResult);
                pass.WriteTexture("DisplacementOutput", displacementCurrent);
                pass.WriteTexture("OceanNormalFoamSmoothnessWrite", normalFoamSmoothness);
                pass.ReadBuffer("OceanData", oceanBuffer);
            }

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Calculate Normals"))
            {
                pass.Initialize(computeShader, 2, settings.Resolution, settings.Resolution, 4);
                pass.WriteTexture("DisplacementInput", displacementCurrent);
                pass.WriteTexture("OceanNormalFoamSmoothnessWrite", normalFoamSmoothness);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetVector("_CascadeTexelSizes", patchSizes / settings.Resolution);
                    pass.SetInt("_OceanTextureSlicePreviousOffset", ((renderGraph.FrameIndex & 1) == 0) ? 0 : 4);
                    pass.SetFloat("Smoothness", settings.Material.GetFloat("_Smoothness"));
                    pass.SetFloat("_FoamStrength", Profile.FoamStrength);
                    pass.SetFloat("_FoamDecay", Profile.FoamDecay);
                    pass.SetFloat("_FoamThreshold", Profile.FoamThreshold);
                    pass.ReadBuffer("OceanData", oceanBuffer);
                });
            }

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Generate Filtered Mips"))
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

                pass.SetRenderFunction((command, pass) =>
                {
                    // TODO: Do this manually? Since this will be compute shader anyway.. could do in same pass
                    command.GenerateMips(displacementCurrent);

                    pass.SetInt("Size", settings.Resolution >> 2);
                    pass.SetFloat("Smoothness", settings.Material.GetFloat("_Smoothness"));
                });
            }

            renderGraph.SetResource(new OceanFftResult(displacementCurrent, displacementHistory, normalFoamSmoothness, lengthToRoughness, oceanBuffer)); ;
        }
    }
}
