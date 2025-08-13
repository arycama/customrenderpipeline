using UnityEngine;

public static class MatrixExtensions
{
	public static Matrix4x4 ConvertToAtlasMatrix(Matrix4x4 m)
	{
		if (SystemInfo.usesReversedZBuffer)
			m.SetRow(2, -m.GetRow(2));

		m.SetRow(0, 0.5f * (m.GetRow(0) + m.GetRow(3)));
		m.SetRow(1, 0.5f * (m.GetRow(1) + m.GetRow(3)));
		m.SetRow(2, 0.5f * (m.GetRow(2) + m.GetRow(3)));
		return m;
	}

	public static Matrix4x4 PixelToNearClip(int width, int height, Vector2 jitter, float tanHalfFov, float aspect, bool flip = false, bool halfTexel = false)
	{
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

	public static Matrix4x4 PixelToWorldViewDirectionMatrix(int width, int height, Vector2 jitter, float tanHalfFov, float aspect, Matrix4x4 viewToWorld, bool flip = false, bool halfTexel = false)
	{
		return viewToWorld * PixelToNearClip(width, height, jitter, tanHalfFov, aspect, flip, halfTexel);
	}
}