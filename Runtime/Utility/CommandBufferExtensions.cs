using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public static class CommandBufferExtensions
{
    public static void DispatchNormalized(this CommandBuffer commandBuffer, ComputeShader computeShader, int kernelIndex, int threadsX, int threadsY, int threadsZ)
    {
        computeShader.GetKernelThreadGroupSizes(kernelIndex, out var x, out var y, out var z);

        var threadGroupsX = Math.DivRoundUp(threadsX, (int)x);
        var threadGroupsY = Math.DivRoundUp(threadsY, (int)y);
        var threadGroupsZ = Math.DivRoundUp(threadsZ, (int)z);

        Assert.IsTrue(threadGroupsX > 0);
        Assert.IsTrue(threadGroupsY > 0);
        Assert.IsTrue(threadGroupsZ > 0);

        commandBuffer.DispatchCompute(computeShader, kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
    }

    /// <summary>
    /// Set Span to GraphicsBuffer without copying
    /// </summary>
    /// <param name="buffer">A GraphicsBuffer</param>
    /// <param name="span">The span data to be set</param>
    /// <typeparam name="T">The type of data</typeparam>
    public unsafe static void SetBufferData<T>(this CommandBuffer command, GraphicsBuffer buffer, ReadOnlySpan<T> span, int length) where T : unmanaged
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        var handle = AtomicSafetyHandle.Create();
#endif

        fixed (void* ptr = span)
        {
            var arr = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(ptr, length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref arr, handle);
#endif
            command.SetBufferData(buffer, arr);
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.Release(handle);
#endif
    }

    public static void SetBufferData<T>(this CommandBuffer command, GraphicsBuffer buffer, ReadOnlySpan<T> span) where T : unmanaged
    {
        command.SetBufferData(buffer, span, span.Length);
    }

    public static void SetBufferData<T>(this CommandBuffer command, GraphicsBuffer buffer, Span<T> span, int length) where T : unmanaged
    {
        command.SetBufferData(buffer, (ReadOnlySpan<T>)span, length);
    }

    public static void SetBufferData<T>(this CommandBuffer command, GraphicsBuffer buffer, Span<T> span) where T : unmanaged
    {
        command.SetBufferData(buffer, span, span.Length);
    }

    public static CommandBufferProfilerScope ProfilerScope(this CommandBuffer command, string name)
    {
        return new CommandBufferProfilerScope(command, name);
    }
}
