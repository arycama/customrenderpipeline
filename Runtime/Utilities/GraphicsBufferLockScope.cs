using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;

public struct GraphicsBufferLockScope<T> : IDisposable where T : struct
{
    private readonly GraphicsBuffer graphicsBuffer;
    private NativeArray<T> data;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetData(int index, T data)
    {
        this.data[index] = data;
    }

	public GraphicsBufferLockScope(GraphicsBuffer graphicsBuffer)
	{
        this.graphicsBuffer = graphicsBuffer;
        data = graphicsBuffer.LockBufferForWrite<T>(0, graphicsBuffer.count);
    }

    void IDisposable.Dispose()
    {
        graphicsBuffer.UnlockBufferAfterWrite<T>(graphicsBuffer.count);
    }
}