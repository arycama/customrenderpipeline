using System;

namespace Arycama.CustomRenderPipeline
{
    public static class MathUtils
    {
        public const float Log2e = (float)1.44269504088896340736;

        public static int DivRoundUp(int x, int y) => (x + y - 1) / y;
        public static float Exp2(float x) => MathF.Pow(2.0f, x);
        public static float Rcp(float x) => 1.0f / x;

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
            return MathF.Floor(value / cellSize) * cellSize;
        }
    }
}
