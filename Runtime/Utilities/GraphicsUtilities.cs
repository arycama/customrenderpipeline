using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public static class GraphicsUtilities
    {
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