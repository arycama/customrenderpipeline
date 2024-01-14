using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public static class GraphicsUtilities
    {
        public static void SafeExpand(ref ComputeBuffer computeBuffer, int size = 1, int stride = sizeof(int), ComputeBufferType type = ComputeBufferType.Default)
        {
            size = Mathf.Max(size, 1);

            if (computeBuffer == null || computeBuffer.count < size)
            {
                if (computeBuffer != null)
                    computeBuffer.Release();

                computeBuffer = new ComputeBuffer(size, stride, type);
            }
        }

        public static void SafeExpand(ref GraphicsBuffer computeBuffer, int size = 1, int stride = sizeof(int), GraphicsBuffer.Target type = GraphicsBuffer.Target.Structured)
        {
            size = Mathf.Max(size, 1);

            if (computeBuffer == null || computeBuffer.count < size)
            {
                if (computeBuffer != null)
                    computeBuffer.Release();

                computeBuffer = new GraphicsBuffer(type, size, stride);
            }
        }

        public static Vector4 ThreadIdScaleOffset(int width, int height)
        {
            return new Vector4((float)(1.0 / width), (float)(1.0 / height), (float)(0.5 / width), (float)(0.5 / height));
        }

        /// <summary>
        /// Calculates a scale and offset for remapping a UV from a 0-1 range to a halfTexel to (1-halfTexel) range
        /// </summary>
        public static Vector4 HalfTexelRemap(float width, float height)
        {
            var invWidth = 1f / width;
            var invHeight = 1f / height;
            return new Vector4(1f - invWidth, 1f - invHeight, 0.5f * invWidth, 0.5f * invHeight);
        }
    }
}