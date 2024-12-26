using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class IndirectComputeRenderPass : BaseComputeRenderPass
    {
        private uint argsOffset;
        private BufferHandle indirectBuffer;

        public void Initialize(ComputeShader computeShader, BufferHandle indirectBuffer, int kernelIndex = 0, uint argsOffset = 0)
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

            command.DispatchCompute(computeShader, kernelIndex, indirectBuffer, argsOffset);

            foreach (var keyword in keywords)
                command.DisableKeyword(computeShader, new LocalKeyword(computeShader, keyword));

            keywords.Clear();
        }
    }
}