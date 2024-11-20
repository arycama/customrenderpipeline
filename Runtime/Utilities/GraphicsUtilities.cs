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
        public static Vector2 HalfTexelRemap(float width)
        {
            var invWidth = 1f / width;
            return new Vector2(1f - invWidth, 0.5f * invWidth);
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

        public static Vector4 HalfTexelRemap(Vector2 position, Vector2 size, Vector2 resolution)
        {
            Vector4 result;
            result.x = (resolution.x - 1f) / (size.x * resolution.x);
            result.y = (resolution.y - 1f) / (size.x * resolution.y);
            result.z = (0.5f * size.x + position.x - position.x * resolution.x) / (size.x * resolution.x);
            result.w = (0.5f * size.y + position.y - position.y * resolution.y) / (size.y * resolution.y);
            return result;
        }

        /// <summary>
        /// Calculates a scale and offset for remapping a UV from a 0-1 range to a halfTexel to (1-halfTexel) range
        /// </summary>
        public static void HalfTexelRemap(float width, float height, float depth, out Vector3 scale, out Vector3 offset)
        {
            var invWidth = 1f / width;
            var invHeight = 1f / height;
            var invDepth = 1f / depth;
            scale = new Vector3(1f - invWidth, 1f - invHeight, 1f - invDepth);
            offset = new Vector3(0.5f * invWidth, 0.5f * invHeight, 0.5f * invDepth);
        }

        public static Vector4 RemapHalfTexelTo01(float width, float height)
        {
            Vector4 result;
            result.x = width / (width - 1f);
            result.y = height / (height - 1f);
            result.z = -0.5f / (width - 1f);
            result.w = -0.5f / (height - 1f);
            return result;
        }

        /// <summary>
        /// Calculates ScaleOffset to Remap a CS thread to UV coordinate that stretches from 0:1. (No half-texel offset)
        /// </summary>
        public static Vector3 ThreadIdScaleOffset01(int width, int height, int depth)
        {
            return new Vector3(1f / (width - 1), 1f / (height - 1), 1f / (depth - 1));
        }

        /// <summary>
        /// Calculates ScaleOffset to Remap a CS thread to UV coordinate that stretches from 0:1. (No half-texel offset)
        /// </summary>
        public static Vector2 ThreadIdScaleOffset01(int width, int height)
        {
            return new Vector2(1f / (width - 1), 1f / (height - 1));
        }

        public static GraphicsBuffer GenerateGridIndexBuffer(int count, bool alternateIndices = false)
        {
            var PatchVertices = count;
            var VerticesPerTileEdge = count + 1;
            var QuadListIndexCount = count * count * 4;

            var indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, QuadListIndexCount, sizeof(ushort));

            var pIndices = new ushort[QuadListIndexCount];
            for (int y = 0, i = 0; y < count; y++)
            {
                var rowStart = y * VerticesPerTileEdge;

                for (var x = 0; x < count; x++, i += 4)
                {
                    // Can do a checkerboard flip to avoid directioanl artifacts, but will mess with the tessellation code
                    var flip = alternateIndices ? (x & 1) == (y & 1) : true;

                    if (flip)
                    {
                        pIndices[i + 0] = (ushort)(rowStart + x);
                        pIndices[i + 1] = (ushort)(rowStart + x + VerticesPerTileEdge);
                        pIndices[i + 2] = (ushort)(rowStart + x + VerticesPerTileEdge + 1);
                        pIndices[i + 3] = (ushort)(rowStart + x + 1);
                    }
                    else
                    {
                        pIndices[i + 0] = (ushort)(rowStart + x + VerticesPerTileEdge);
                        pIndices[i + 1] = (ushort)(rowStart + x + VerticesPerTileEdge + 1);
                        pIndices[i + 2] = (ushort)(rowStart + x + 1);
                        pIndices[i + 3] = (ushort)(rowStart + x);
                    }
                }
            }

            indexBuffer.SetData(pIndices);
            return indexBuffer;
        }
    }
}