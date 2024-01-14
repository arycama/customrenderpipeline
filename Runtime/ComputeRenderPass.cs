using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ComputeRenderPass : RenderPass
    {
        private ComputeShader computeShader;
        private int kernelIndex;

        public ComputeRenderPass(ComputeShader computeShader, int kernelIndex)
        {
            this.computeShader = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            this.kernelIndex = kernelIndex;
        }

        public override void SetTexture(CommandBuffer command, string propertyName, Texture texture)
        {
            command.SetComputeTextureParam(computeShader, kernelIndex, propertyName, texture);
        }

        public override void SetTexture(CommandBuffer command, string propertyName, RTHandle texture)
        {
            command.SetComputeTextureParam(computeShader, kernelIndex, propertyName, texture);
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, GraphicsBuffer buffer)
        {
            throw new NotImplementedException();
        }

        public override void SetVector(CommandBuffer command, string propertyName, Vector4 value)
        {
            command.SetComputeVectorParam(computeShader, propertyName, value);
        }

        public override void SetFloat(CommandBuffer command, string propertyName, float value)
        {
            command.SetComputeFloatParam(computeShader, propertyName, value);
        }

        public override void SetInt(CommandBuffer command, string propertyName, int value)
        {
            command.SetComputeIntParam(computeShader, propertyName, value);
        }
    }
}