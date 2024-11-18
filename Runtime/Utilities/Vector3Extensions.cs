using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public static class Vector3Extensions
    {
        public static Vector3 Y0(this Vector3 v) => new Vector3(v.x, 0f, v.z);

        public static Vector2 XZ(this Vector3 v) => new Vector3(v.x, v.z);
        public static Vector2 XY(this Vector3 v) => new Vector3(v.x, v.y);
    }
}