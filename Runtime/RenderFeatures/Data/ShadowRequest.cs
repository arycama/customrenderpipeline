using UnityEngine;
using UnityEngine.Rendering;

public readonly struct ShadowRequest
{
	public int LightIndex { get; }
	public Matrix4x4 ViewMatrix { get; }
	public Matrix4x4 ProjectionMatrix { get; }
	public ShadowSplitData ShadowSplitData { get; }
	public int CubemapFace { get; }
	public Float3 LightPosition { get; }
	public bool HasCasters { get; }
	public float Near { get; }
	public float Far { get; }
	public Float3 ViewPosition { get; }
	public Quaternion ViewRotation { get; }
	public float Width { get; }
	public float Height { get; }

	public ShadowRequest(int lightIndex, Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix, ShadowSplitData shadowSplitData, int cubemapFace, Float3 lightPosition, bool hasCasters, float near, float far, Float3 viewPosition, Quaternion viewRotation, float width, float height)
	{
		LightIndex = lightIndex;
		ViewMatrix = viewMatrix;
		ProjectionMatrix = projectionMatrix;
		ShadowSplitData = shadowSplitData;
		CubemapFace = cubemapFace;
		LightPosition = lightPosition;
		HasCasters = hasCasters;
		Near = near;
		Far = far;
		ViewPosition = viewPosition;
		ViewRotation = viewRotation;
		Width = width;
		Height = height;
	}
}
