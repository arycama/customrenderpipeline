using UnityEngine;
using UnityEngine.Rendering;

public readonly struct VirtualTextureData : IRenderPassData
{
	public readonly Texture2DArray albedoSmoothness, normal, height;
	public readonly ResourceHandle<RenderTexture> indirection;
	public readonly ResourceHandle<GraphicsBuffer> feedbackBuffer;
	public readonly float anisoLevel, virtualResolution;

	public VirtualTextureData(Texture2DArray albedoSmoothness, Texture2DArray normal, Texture2DArray height, ResourceHandle<RenderTexture> indirection, ResourceHandle<GraphicsBuffer> feedbackBuffer, float anisoLevel, float virtualResolution)
	{
		this.albedoSmoothness = albedoSmoothness;
		this.normal = normal;
		this.height = height;
		this.indirection = indirection;
		this.anisoLevel = anisoLevel;
		this.virtualResolution = virtualResolution;
		this.feedbackBuffer = feedbackBuffer;
	}

	void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadTexture("IndirectionTexture", indirection);

		pass.WriteBuffer("VirtualFeedbackTexture", feedbackBuffer);
		pass.ReadBuffer("VirtualFeedbackTexture", feedbackBuffer);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetTexture("VirtualTexture", albedoSmoothness);
		pass.SetTexture("VirtualNormalTexture", normal);
		pass.SetTexture("VirtualHeightTexture", height);
		pass.SetFloat("AnisoLevel", anisoLevel);
		pass.SetFloat("VirtualUvScale", virtualResolution);
		command.SetRandomWriteTarget(6, pass.GetBuffer(feedbackBuffer));
	}
}
