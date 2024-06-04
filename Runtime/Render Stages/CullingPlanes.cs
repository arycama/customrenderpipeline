using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public struct CullingPlanes
    {
        private const int MaximumCullingPlaneCount = 10;
        private unsafe fixed byte cullingPlanes[160];
        private int count;

        public int Count
        {
            readonly get
            {
                return count;
            }
            set
            {
                if (value < 0 || value > 10)
                    throw new ArgumentException($"Value should range from {0} to ShadowSplitData.maximumCullingPlaneCount ({10}), but was {value}.");

                count = value;
            }
        }

        public unsafe Plane GetCullingPlane(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentException("index", $"Index should be at least {0} and less than cullingPlaneCount ({Count}), but was {index}.");

            fixed (byte* ptr = cullingPlanes)
            {
                Plane* ptr2 = (Plane*)ptr;
                return ptr2[index];
            }
        }

        public unsafe Vector4 GetCullingPlaneVector4(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentException("index", $"Index should be at least {0} and less than cullingPlaneCount ({Count}), but was {index}.");

            fixed (byte* ptr = cullingPlanes)
            {
                Vector4* ptr2 = (Vector4*)ptr;
                return ptr2[index];
            }
        }

        public unsafe void SetCullingPlane(int index, Plane plane)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentException("index", $"Index should be at least {0} and less than cullingPlaneCount ({Count}), but was {index}.");

            fixed (byte* ptr = cullingPlanes)
            {
                Plane* ptr2 = (Plane*)ptr;
                ptr2[index] = plane;
            }
        }
    }
}