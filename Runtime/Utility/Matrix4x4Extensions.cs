using UnityEngine;

public static class Matrix4x4Extensions
{
	public static Vector3 Forward(this Matrix4x4 matrix) => matrix.GetColumn(2);

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
		return WorldToLocal(rotation.Right, rotation.Up, rotation.Forward, position);
	}
}