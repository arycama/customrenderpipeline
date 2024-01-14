﻿using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ComputeRenderPass : RenderPass
    {
        private ComputeShader computeShader;
        private int kernelIndex, xThreads, yThreads, zThreads;

        public ComputeRenderPass(ComputeShader computeShader, int kernelIndex, int xThreads, int yThreads = 1, int zThreads = 1)
        {
            this.computeShader = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            this.kernelIndex = kernelIndex;
            this.xThreads = xThreads;
            this.yThreads = yThreads;
            this.zThreads = zThreads;
        }

        public override void SetTexture(CommandBuffer command, string propertyName, Texture texture)
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

        public override void Execute(CommandBuffer command)
        {
            command.DispatchNormalized(computeShader, kernelIndex, xThreads, yThreads, zThreads);
        }
    }
}