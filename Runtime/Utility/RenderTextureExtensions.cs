using UnityEngine;

public static class RenderTextureExtensions
{
	public static RenderTexture Created(this RenderTexture renderTexture)
	{
		if (!renderTexture.IsCreated())
			_ = renderTexture.Create();

		return renderTexture;
	}
}
