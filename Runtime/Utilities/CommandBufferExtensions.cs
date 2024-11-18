using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public static class CommandBufferExtensions
    {
        public static void DispatchNormalized(this CommandBuffer commandBuffer, ComputeShader computeShader, int kernelIndex, int threadsX, int threadsY, int threadsZ)
        {
            computeShader.GetKernelThreadGroupSizes(kernelIndex, out var x, out var y, out var z);

            var threadGroupsX = MathUtils.DivRoundUp(threadsX, (int)x);
            var threadGroupsY = MathUtils.DivRoundUp(threadsY, (int)y);
            var threadGroupsZ = MathUtils.DivRoundUp(threadsZ, (int)z);

            Assert.IsTrue(threadGroupsX > 0);
            Assert.IsTrue(threadGroupsY > 0);
            Assert.IsTrue(threadGroupsZ > 0);

            commandBuffer.DispatchCompute(computeShader, kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
        }

        public static CommandBufferProfilerScope BeginScopedSample(this CommandBuffer command, string name)
        {
            return new CommandBufferProfilerScope(command, name);
        }

        public static void EnableShaderKeywordConditional(this CommandBuffer commandBuffer, string keyword, bool enable)
        {
            if (enable)
                commandBuffer.EnableShaderKeyword(keyword);
        }

        public static void DisableShaderKeywordConditional(this CommandBuffer commandBuffer, string keyword, bool disable)
        {
            if (disable)
                commandBuffer.DisableShaderKeyword(keyword);
        }

        public static CommandBufferKeywordScope KeywordScope(this CommandBuffer commandBuffer, string keyword)
        {
            return new CommandBufferKeywordScope(commandBuffer, keyword);
        }

        public static CommandBufferConditionalKeywordScope KeywordScope(this CommandBuffer commandBuffer, string keyword, bool isEnabled)
        {
            return new CommandBufferConditionalKeywordScope(commandBuffer, keyword, isEnabled);
        }
    }
}
