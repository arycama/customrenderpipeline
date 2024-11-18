using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct Matrix3x4
    {
        private readonly float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23;

        public readonly float this[int index]
        {
            get
            {
                return index switch
                {
                    0 => m00,
                    1 => m01,
                    2 => m02,
                    3 => m03,
                    4 => m10,
                    5 => m11,
                    6 => m12,
                    7 => m13,
                    8 => m20,
                    9 => m21,
                    10 => m22,
                    11 => m23,
                    _ => throw new ArgumentOutOfRangeException(nameof(index)),
                };
            }
        }

        public Matrix3x4(float m00, float m01, float m02, float m03, float m10, float m11, float m12, float m13, float m20, float m21, float m22, float m23)
        {
            this.m00 = m00;
            this.m01 = m01;
            this.m02 = m02;
            this.m03 = m03;
            this.m10 = m10;
            this.m11 = m11;
            this.m12 = m12;
            this.m13 = m13;
            this.m20 = m20;
            this.m21 = m21;
            this.m22 = m22;
            this.m23 = m23;
        }

        public static implicit operator Matrix3x4(Matrix4x4 m)
        {
            return new Matrix3x4(m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23);
        }
    }
}