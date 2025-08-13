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

	public ShadowRequest(int lightIndex, Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix, ShadowSplitData shadowSplitData, int cubemapFace, Float3 lightPosition)
	{
		LightIndex = lightIndex;
		ViewMatrix = viewMatrix;
		ProjectionMatrix = projectionMatrix;
		ShadowSplitData = shadowSplitData;
		CubemapFace = cubemapFace;
		LightPosition = lightPosition;
	}
}
