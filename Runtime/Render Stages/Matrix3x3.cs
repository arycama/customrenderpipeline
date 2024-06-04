using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public struct Matrix3x3
    {
        float m00, m01, m02, m10, m11, m12, m20, m21, m22;

        public float this[int index]
        {
            readonly get
            {
                return index switch
                {
                    0 => m00,
                    1 => m01,
                    2 => m02,
                    3 => m10,
                    4 => m11,
                    5 => m12,
                    6 => m20,
                    7 => m21,
                    8 => m22,
                    _ => throw new ArgumentOutOfRangeException(nameof(index)),
                };
            }

            set
            {
                switch(index)
                {
                    case 0:
                        m00 = value;
                        break;
                    case 1:
                        m01 = value;
                        break;
                    case 2:
                        m02 = value;
                        break;
                    case 3:
                        m10 = value;
                        break;
                    case 4:
                        m11 = value;
                        break;
                    case 5:
                        m12 = value;
                        break;
                    case 6:
                        m20 = value;
                        break;
                    case 7:
                        m21 = value;
                        break;
                    case 8:
                        m22 = value;
                        break;
                }
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public readonly Vector3 GetRow(int index)
        {
            return index switch
            {
                0 => new Vector3(m00, m01, m02),
                1 => new Vector3(m10, m11, m12),
                2 => new Vector3(m20, m21, m22),
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
        }

        public Matrix3x3(float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22)
        {
            this.m00 = m00;
            this.m01 = m01;
            this.m02 = m02;
            this.m10 = m10;
            this.m11 = m11;
            this.m12 = m12;
            this.m20 = m20;
            this.m21 = m21;
            this.m22 = m22;
        }
    }
}

