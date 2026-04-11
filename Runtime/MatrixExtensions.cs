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
}