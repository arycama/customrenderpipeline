﻿using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

        public static void ExpandAndSetComputeBufferData<T>(this CommandBuffer command, ref ComputeBuffer computeBuffer, List<T> data, ComputeBufferType type = ComputeBufferType.Default) where T : struct
        {
            var size = Mathf.Max(data.Count, 1);

            if (computeBuffer == null || computeBuffer.count < size)
            {
                if (computeBuffer != null)
                    computeBuffer.Release();

                var stride = UnsafeUtility.SizeOf<T>();
                computeBuffer = new ComputeBuffer(size, stride, type);
            }

            command.SetBufferData(computeBuffer, data);
        }

        public static void ExpandAndSetComputeBufferData<T>(this CommandBuffer command, ref ComputeBuffer computeBuffer, NativeArray<T> data, ComputeBufferType type = ComputeBufferType.Default) where T : struct
        {
            var size = Mathf.Max(data.Length, 1);

            if (computeBuffer == null || computeBuffer.count < size)
            {
                if (computeBuffer != null)
                    computeBuffer.Release();

                var stride = UnsafeUtility.SizeOf<T>();
                computeBuffer = new ComputeBuffer(size, stride, type);
            }

            command.SetBufferData(computeBuffer, data);
        }
    }
}
