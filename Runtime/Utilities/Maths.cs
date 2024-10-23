using System;
using UnityEngine;
using static System.MathF;

namespace Arycama.CustomRenderPipeline
{
    public static class Maths
    {
        public const float Log2e = (float)1.44269504088896340736;

        public static int DivRoundUp(int x, int y) => (x + y - 1) / y;
        public static float Exp2(float x) => Pow(2.0f, x);
        public static float Exp10(float x) => Pow(10.0f, x);
        public static float Rcp(float x) => 1.0f / x;
        public static float Log2(float x) => Log(x, 2.0f);

        public static int MipCount(int width, int height)
        {
            return (int)Math.Log(Math.Max(width, height), 2) + 1;
        }

        public static int MipCount(int width, int height, int depth)
        {
            return (int)Math.Log(Math.Max(width, Math.Max(height, depth)), 2) + 1;
        }

        public static float Snap(float value, float cellSize)
        {
            return Floor(value / cellSize) * cellSize;
        }

        public static Vector3 Mul(Matrix3x3 m, Vector3 v)
        {
            Vector3 res;
            res.x = v.x * m[0] + v.y * m[1] + v.z * m[2];
            res.y = v.x * m[3] + v.y * m[4] + v.z * m[5];
            res.z = v.x * m[6] + v.y * m[7] + v.z * m[8];
            return res;
        }

        public static Vector3 Max(Vector3 x, float y)
        {
            return new Vector3(MathF.Max(x.x, y), MathF.Max(x.y, y), MathF.Max(x.z, y));
        }

        public static Vector3 Clamp(Vector3 x, float min, float max)
        {
            Vector3 result;
            result.x = Math.Clamp(x.x, min, max);
            result.y = Math.Clamp(x.y, min, max);
            result.z = Math.Clamp(x.z, min, max);
            return result;
        }

        public static float Square(float x) => x * x;
        public static int Square(int x) => x * x;
    }
}
