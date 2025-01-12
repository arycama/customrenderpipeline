using UnityEngine;

public static class GraphicsBufferExtensions
{
    public static GraphicsBufferLockScope<T> DirectWrite<T>(this GraphicsBuffer buffer) where T : struct
    {
        return new GraphicsBufferLockScope<T>(buffer);
    }
}
