using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    public class ComputeRenderPass<T> : BaseComputeRenderPass<T>
    {
        private int xThreads, yThreads, zThreads;
        private bool normalizedDispatch;

        public void Initialize(ComputeShader computeShader, int kernelIndex = 0, int xThreads = 1, int yThreads = 1, int zThreads = 1, bool normalizedDispatch = true)
        {
            this.computeShader = computeShader;
            this.kernelIndex = kernelIndex;
            this.xThreads = xThreads;
            this.yThreads = yThreads;
            this.zThreads = zThreads;
            this.normalizedDispatch = normalizedDispatch;
        }

        protected override void Execute()
        {
            for (var i = 0; i < colorBindings.Count; i++)
                Command.SetComputeTextureParam(computeShader, kernelIndex, colorBindings[i].Item2, GetRenderTexture(colorBindings[i].Item1), colorBindings[i].Item3);

            foreach (var keyword in keywords)
                Command.EnableShaderKeyword(keyword);

            if (normalizedDispatch)
                Command.DispatchNormalized(computeShader, kernelIndex, xThreads, yThreads, zThreads);
            else
            {
                Assert.IsTrue(xThreads > 0);
                Assert.IsTrue(yThreads > 0);
                Assert.IsTrue(zThreads > 0);

                Command.DispatchCompute(computeShader, kernelIndex, xThreads, yThreads, zThreads);
            }

            foreach (var keyword in keywords)
                Command.DisableShaderKeyword(keyword);
        }
    }
}