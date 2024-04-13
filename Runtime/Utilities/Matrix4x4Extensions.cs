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

        public static Matrix4x4 ConvertToAtlasMatrix(this Matrix4x4 m, bool reverseZ = true)
        {
            if (reverseZ && SystemInfo.usesReversedZBuffer)
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

        public static Matrix4x4 PixelToNearClip(int width, int height, Vector2 jitter, float fov, float aspect, bool flip = false, bool halfTexel = false)
        {
            var tanHalfFov = Mathf.Tan(0.5f * fov * Mathf.Deg2Rad);

            var m00 = aspect * tanHalfFov * 2.0f / width;
            var m11 = tanHalfFov * 2.0f / height;
            var m12 = -(tanHalfFov + tanHalfFov * jitter.y);

            return new Matrix4x4
            {
                m00 = m00,
                m11 = flip ? -m11 : m11,
                m02 = -(aspect * tanHalfFov + tanHalfFov * aspect * jitter.x) + (halfTexel ? (0.5f * m00) : 0.0f),
                m12 = (flip ? -m12 : m12) + (halfTexel ? 0.5f * (flip ? -m11 : m11) : 0.0f),
                m22 = 1.0f,
                m33 = 1.0f
            };
        }

        public static Matrix4x4 PixelToWorldViewDirectionMatrix(int width, int height, Vector2 jitter, float fov, float aspect, Matrix4x4 viewToWorld, bool flip = false, bool halfTexel = false)
        {
            return viewToWorld * PixelToNearClip(width, height, jitter, fov, aspect, flip, halfTexel);
        }

        // Returns an ortho matrix where the view is centered in the XY, and at 0 in the Z
        public static Matrix4x4 OrthoCentered(float width, float height, float depth)
        {
            return new Matrix4x4
            {
                m00 = 2.0f / width,
                m11 = 2.0f / height,
                m22 = 1.0f / depth,
                m23 = -1.0f,
                m33 = 1.0f
            };
        }

        public static Matrix4x4 OrthoOffCenter(float left, float right, float bottom, float top, float near, float far) => new ()
        {
            m00 = 2.0f / (right - left),
            m03 = (right + left) / (left - right),
            m11 = 2.0f / (top - bottom),
            m13 = (top + bottom) / (bottom - top),
            m22 = 2.0f / (far - near),
            m23 = (far + near) / (near - far),
            m33 = 1.0f
        };

        // Similar to above, but maps X and Y between 0 and 1 instead of -1 to 1
        public static Matrix4x4 OrthoOffCenterNormalized(float left, float right, float bottom, float top, float near, float far) => new()
        {
            m00 = 1.0f / (right - left),
            m03 = left / (left - right),
            m11 = 1.0f / (top - bottom),
            m13 = bottom / (bottom - top),
            m22 = 1.0f / (far - near),
            m23 = near / (near - far),
            m33 = 1.0f
        };

        public static Matrix4x4 OrthoOffCenterInverse(float left, float right, float bottom, float top, float near, float far) => new()
        {
            m00 = (right - left) * 0.5f,
            m03 = (right + left) * 0.5f,
            m11 = (top - bottom) * 0.5f,
            m13 = (top + bottom) * 0.5f,
            m22 = far - near,
            m23 = near,
            m33 = 1.0f
        };

        //public static float4x4 PerspectiveFov(float verticalFov, float aspect, float near, float far)
        //{
        //    float cotangent = 1.0f / tan(verticalFov * 0.5f);
        //    float rcpdz = 1.0f / (near - far);

        //    return float4x4(
        //        cotangent / aspect, 0.0f, 0.0f, 0.0f,
        //        0.0f, cotangent, 0.0f, 0.0f,
        //        0.0f, 0.0f, (far + near) * rcpdz, 2.0f * near * far * rcpdz,
        //        0.0f, 0.0f, -1.0f, 0.0f
        //        );
        //}

        //public static float4x4 PerspectiveOffCenter(float left, float right, float bottom, float top, float near, float far)
        //{
        //    float rcpdz = 1.0f / (near - far);
        //    float rcpWidth = 1.0f / (right - left);
        //    float rcpHeight = 1.0f / (top - bottom);

        //    return float4x4(
        //        2.0f * near * rcpWidth, 0.0f, (left + right) * rcpWidth, 0.0f,
        //        0.0f, 2.0f * near * rcpHeight, (bottom + top) * rcpHeight, 0.0f,
        //        0.0f, 0.0f, (far + near) * rcpdz, 2.0f * near * far * rcpdz,
        //        0.0f, 0.0f, -1.0f, 0.0f
        //        );
        //}
    }
}
