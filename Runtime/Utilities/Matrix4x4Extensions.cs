using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public static class Matrix4x4Extensions
    {
        public static float Near(this Matrix4x4 matrix) => matrix[2, 3] / (matrix[2, 2] - 1f);
        public static float Far(this Matrix4x4 matrix) => matrix[2, 3] / (matrix[2, 2] + 1f);
        public static float Fov(this Matrix4x4 matrix) => matrix[1, 1];
        public static float Aspect(this Matrix4x4 matrix) => matrix.m11 / matrix.m00;
        public static float OrthoWidth(this Matrix4x4 matrix) => 2f / matrix.m00;
        public static float OrthoHeight(this Matrix4x4 matrix) => 2f / matrix.m11;
        public static float OrthoNear(this Matrix4x4 matrix) => (1f + matrix.m23) / matrix.m22;
        public static float OrthoFar(this Matrix4x4 matrix) => (matrix.m23 - 1f) / matrix.m22;

        public static Vector3 Right(this Matrix4x4 matrix) => matrix.GetColumn(0);

        public static Vector3 Up(this Matrix4x4 matrix) => matrix.GetColumn(1);

        public static Vector3 Forward(this Matrix4x4 matrix) => matrix.GetColumn(2);

        public static Vector3 Position(this Matrix4x4 matrix) => matrix.GetColumn(3);

        public static Matrix4x4 ConvertToAtlasMatrix(this Matrix4x4 m)
        {
            if (SystemInfo.usesReversedZBuffer)
                m.SetRow(2, -m.GetRow(2));

            m.SetRow(0, 0.5f * (m.GetRow(0) + m.GetRow(3)));
            m.SetRow(1, 0.5f * (m.GetRow(1) + m.GetRow(3)));
            m.SetRow(2, 0.5f * (m.GetRow(2) + m.GetRow(3)));
            return m;
        }

        public static Matrix4x4 WorldToLocal(Vector3 right, Vector3 up, Vector3 forward, Vector3 position)
        {
            var c0 = new Vector4(right.x, up.x, forward.x, 0f);
            var c1 = new Vector4(right.y, up.y, forward.y, 0f);
            var c2 = new Vector4(right.z, up.z, forward.z, 0f);
            var c3 = new Vector4(-Vector3.Dot(right, position), -Vector3.Dot(up, position), -Vector3.Dot(forward, position), 1f);
            return new Matrix4x4(c0, c1, c2, c3);
        }

        public static Matrix4x4 WorldToLocal(Vector3 position, Quaternion rotation)
        {
            return WorldToLocal(rotation.Right(), rotation.Up(), rotation.Forward(), position);
        }

        public static Matrix4x4 PixelToWorldViewDirectionMatrix(int width, int height, Vector2 jitter, float fov, float aspect, Matrix4x4 viewToWorld, bool flip = false)
        {
            // Compose the view space version first.
            // V = -(X, Y, Z), s.t. Z = 1,
            // X = (2x / resX - 1) * tan(vFoV / 2) * ar = x * [(2 / resX) * tan(vFoV / 2) * ar] + [-tan(vFoV / 2) * ar] = x * [-m00] + [-m20]
            // Y = (2y / resY - 1) * tan(vFoV / 2)      = y * [(2 / resY) * tan(vFoV / 2)]      + [-tan(vFoV / 2)]      = y * [-m11] + [-m21]
            var tanHalfVertFoV = Mathf.Tan(0.5f * fov * Mathf.Deg2Rad);

            // Compose the matrix.
            var m11 = -2f / height * tanHalfVertFoV;
            var m21 = (1f - jitter.y) * tanHalfVertFoV;

            if (flip)
            {
                // Flip Y.
                m11 = -m11;
                m21 = -m21;
            }

            var viewSpaceRasterTransform = new Matrix4x4
            {
                m00 = -2f / width * tanHalfVertFoV * aspect,
                m11 = m11,
                m02 = (1f + jitter.x) * tanHalfVertFoV * aspect,
                m12 = m21,
                m22 = -1.0f,
                m33 = 1.0f
            };

            return viewToWorld * viewSpaceRasterTransform;
        }
    }
}
