﻿using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class IndirectComputeRenderPass : BaseComputeRenderPass
    {
        private uint argsOffset;
        private ResourceHandle<GraphicsBuffer> indirectBuffer;

        public void Initialize(ComputeShader computeShader, ResourceHandle<GraphicsBuffer> indirectBuffer, int kernelIndex = 0, uint argsOffset = 0)
        {
            this.computeShader = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            this.kernelIndex = kernelIndex;
            this.indirectBuffer = indirectBuffer;
            this.argsOffset = argsOffset;

            ReadBuffer("_IndirectArgsInput", indirectBuffer);
        }

        protected override void Execute()
        {
            foreach (var keyword in keywords)
                command.EnableKeyword(computeShader, new LocalKeyword(computeShader, keyword));

            command.DispatchCompute(computeShader, kernelIndex, GetBuffer(indirectBuffer), argsOffset);

            foreach (var keyword in keywords)
                command.DisableKeyword(computeShader, new LocalKeyword(computeShader, keyword));

            keywords.Clear();
        }
    }
}