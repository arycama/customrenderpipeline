using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public class WaterFft : RenderFeature
    {
        private const int CascadeCount = 4;
        private static readonly IndexedShaderPropertyId smoothnessMapIds = new("SmoothnessOutput");

        private WaterSystem.Settings settings;
        private GraphicsBuffer spectrumBuffer, dispersionBuffer;
        private RTHandle lengthToRoughness;
        private bool disposedValue;
        private bool roughnessInitialized;
        private RTHandle displacementCurrent;

        private WaterProfile Profile => settings.Profile;

        public WaterFft(RenderGraph renderGraph, WaterSystem.Settings settings) : base(renderGraph)
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

        public void Render(double time)
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

                    var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                    {
                        pass.SetFloat(command, "_MaxIterations", 32);
                        pass.SetFloat(command, "_Resolution", 256);
                    });
                }

                roughnessInitialized = true;
            }

            // Calculate constants
            var rcpScales = new Vector4(1f / Mathf.Pow(Profile.CascadeScale, 0f), 1f / Mathf.Pow(Profile.CascadeScale, 1f), 1f / Mathf.Pow(Profile.CascadeScale, 2f), 1f / Mathf.Pow(Profile.CascadeScale, 3f));
            var patchSizes = new Vector4(Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 0f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 1f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 2f), Profile.PatchSize / Mathf.Pow(Profile.CascadeScale, 3f));
            var spectrumStart = new Vector4(0, Profile.MaxWaveNumber * patchSizes.y / patchSizes.x, Profile.MaxWaveNumber * patchSizes.z / patchSizes.y, Profile.MaxWaveNumber * patchSizes.w / patchSizes.z);
            var spectrumEnd = new Vector4(Profile.MaxWaveNumber, Profile.MaxWaveNumber, Profile.MaxWaveNumber, settings.Resolution);
            var oceanScale = new Vector4(1f / patchSizes.x, 1f / patchSizes.y, 1f / patchSizes.z, 1f / patchSizes.w);
            var rcpTexelSizes = new Vector4(settings.Resolution / patchSizes.x, settings.Resolution / patchSizes.y, settings.Resolution / patchSizes.z, settings.Resolution / patchSizes.w);
            var texelSizes = patchSizes / settings.Resolution;

            // Load resources
            var computeShader = Resources.Load<ComputeShader>("OceanFFT");
            var oceanBuffer = renderGraph.SetConstantBuffer((Profile.WindSpeed, Profile.WindAngle, Profile.Fetch, Profile.SpreadBlend, Profile.Swell, Profile.PeakEnhancement, Profile.ShortWavesFade));

            // Update spectrum (TODO: Only when properties change)
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Spectrum"))
            {
                pass.Initialize(computeShader, 4, settings.Resolution, settings.Resolution, 4);
                //pass.WriteBuffer("OceanSpectrum", spectrumBuffer);
                pass.ReadBuffer("OceanData", oceanBuffer);

                var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    pass.SetBuffer(command, "OceanSpectrumWrite", spectrumBuffer);
                    pass.SetBuffer(command, "OceanDispersionWrite", dispersionBuffer);
                    pass.SetVector(command, "_OceanScale", oceanScale);
                    pass.SetVector(command, "SpectrumStart", spectrumStart);
                    pass.SetVector(command, "SpectrumEnd", spectrumEnd);
                    pass.SetFloat(command, "_OceanGravity", Profile.Gravity);
                    pass.SetFloat(command, "_WindSpeed", Profile.WindSpeed);
                    pass.SetFloat(command, "SequenceLength", Profile.SequenceLength);
                    pass.SetFloat(command, "TimeScale", Profile.TimeScale);
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

                var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    pass.SetBuffer(command, "OceanSpectrum", spectrumBuffer);
                    pass.SetBuffer(command, "OceanDispersion", dispersionBuffer);
                    pass.SetVector(command, "_OceanScale", oceanScale);
                    pass.SetVector(command, "SpectrumStart", spectrumStart);
                    pass.SetVector(command, "SpectrumEnd", spectrumEnd);
                    pass.SetFloat(command, "_OceanGravity", Profile.Gravity);
                    pass.SetFloat(command, "_WindSpeed", Profile.WindSpeed);
                    pass.SetFloat(command, "SequenceLength", Profile.SequenceLength);
                    pass.SetFloat(command, "TimeScale", Profile.TimeScale);
                    pass.SetFloat(command, "Time", (float)time);
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
                pass.WriteTexture("OceanNormalFoamSmoothness", normalFoamSmoothness);
            }

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Calculate Normals"))
            {
                pass.Initialize(computeShader, 2, settings.Resolution, settings.Resolution, 4);
                pass.WriteTexture("DisplacementInput", displacementCurrent);
                pass.WriteTexture("OceanNormalFoamSmoothness", normalFoamSmoothness);

                var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    pass.SetVector(command, "_CascadeTexelSizes", texelSizes);
                    pass.SetInt(command, "_OceanTextureSlicePreviousOffset", ((renderGraph.FrameIndex & 1) == 0) ? 0 : 4);
                    pass.SetFloat(command, "Smoothness", settings.Material.GetFloat("_Smoothness"));
                    pass.SetFloat(command, "_FoamStrength", Profile.FoamStrength);
                    pass.SetFloat(command, "_FoamDecay", Profile.FoamDecay);
                    pass.SetFloat(command, "_FoamThreshold", Profile.FoamThreshold);
                });
            }

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Generate Filtered Mips"))
            {
                pass.Initialize(computeShader, 3, (settings.Resolution * 4) >> 2, (settings.Resolution) >> 2, 1);
                pass.ReadTexture("_LengthToRoughness", lengthToRoughness);

                var mipCount = (int)Mathf.Log(settings.Resolution, 2) + 1;
                for (var j = 0; j < mipCount; j++)
                {
                    var smoothnessId = smoothnessMapIds.GetProperty(j);
                    pass.WriteTexture(smoothnessId, normalFoamSmoothness, j);
                }

                var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    // TODO: Do this manually? Since this will be compute shader anyway.. could do in same pass
                    command.GenerateMips(displacementCurrent);

                    pass.SetInt(command, "Size", settings.Resolution >> 2);
                    pass.SetFloat(command, "Smoothness", settings.Material.GetFloat("_Smoothness"));
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new OceanFftResult(displacementCurrent, displacementHistory, normalFoamSmoothness, lengthToRoughness), renderGraph.FrameIndex);
        }
    }
}
