using UnityEngine;
using UnityEngine.Rendering;

public readonly struct VirtualTextureData : IRenderPassData
{
	private static readonly int VirtualTextureId = Shader.PropertyToID("VirtualTexture");
	private static readonly int VirtualNormalTextureId = Shader.PropertyToID("VirtualNormalTexture");
	private static readonly int VirtualHeightTextureId = Shader.PropertyToID("VirtualHeightTexture");

	public readonly Texture2DArray albedoSmoothness, normal, height;
	public readonly ResourceHandle<RenderTexture> indirection;
	public readonly ResourceHandle<GraphicsBuffer> feedbackBuffer;
	public readonly float anisoLevel, virtualTextureSize, indirectionTextureSize, rcpIndirectionTextureSize;

	public VirtualTextureData(Texture2DArray albedoSmoothness, Texture2DArray normal, Texture2DArray height, ResourceHandle<RenderTexture> indirection, ResourceHandle<GraphicsBuffer> feedbackBuffer, float anisoLevel, float virtualTextureSize, float indirectionTextureSize, float rcpIndirectionTextureSize)
	{
		this.albedoSmoothness = albedoSmoothness;
		this.normal = normal;
		this.height = height;
		this.indirection = indirection;
		this.anisoLevel = anisoLevel;
		this.virtualTextureSize = virtualTextureSize;
		this.feedbackBuffer = feedbackBuffer;
		this.indirectionTextureSize = indirectionTextureSize;
		this.rcpIndirectionTextureSize = rcpIndirectionTextureSize;
	}

	void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadTexture("IndirectionTexture", indirection);
		pass.WriteBuffer("VirtualFeedbackTexture", feedbackBuffer);
		pass.ReadBuffer("VirtualFeedbackTexture", feedbackBuffer);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetTexture(VirtualTextureId, albedoSmoothness);
		pass.SetTexture(VirtualNormalTextureId, normal);
		pass.SetTexture(VirtualHeightTextureId, height);
		pass.SetFloat("AnisoLevel", anisoLevel);
		pass.SetFloat("VirtualTextureSize", virtualTextureSize);
		pass.SetFloat("IndirectionTextureSize", indirectionTextureSize);
		pass.SetFloat("RcpIndirectionTextureSize", rcpIndirectionTextureSize);
		command.SetRandomWriteTarget(6, pass.GetBuffer(feedbackBuffer));
	}
}
