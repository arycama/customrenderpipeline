using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ComputeRenderPass : BaseComputeRenderPass
    {
        private int xThreads, yThreads, zThreads;
        private bool normalizedDispatch;

        public void Initialize(ComputeShader computeShader, int kernelIndex = 0, int xThreads = 1, int yThreads = 1, int zThreads = 1, bool normalizedDispatch = true)
        {
            this.computeShader = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            this.kernelIndex = kernelIndex;
            this.xThreads = xThreads;
            this.yThreads = yThreads;
            this.zThreads = zThreads;
            this.normalizedDispatch = normalizedDispatch;
        }

        protected override void Execute()
        {
            foreach (var keyword in keywords)
                command.EnableKeyword(computeShader, new LocalKeyword(computeShader, keyword));

            if (normalizedDispatch)
                command.DispatchNormalized(computeShader, kernelIndex, xThreads, yThreads, zThreads);
            else
            {
                Assert.IsTrue(xThreads > 0);
                Assert.IsTrue(yThreads > 0);
                Assert.IsTrue(zThreads > 0);

                command.DispatchCompute(computeShader, kernelIndex, xThreads, yThreads, zThreads);
            }

            foreach (var keyword in keywords)
                command.DisableKeyword(computeShader, new LocalKeyword(computeShader, keyword));

            keywords.Clear();
        }
    }
}