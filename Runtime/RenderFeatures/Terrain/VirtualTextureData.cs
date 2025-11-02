using UnityEngine;
using UnityEngine.Rendering;

public readonly struct VirtualTextureData : IRenderPassData
{
	private static readonly int VirtualTextureId = Shader.PropertyToID("VirtualTexture");
	private static readonly int VirtualNormalTextureId = Shader.PropertyToID("VirtualNormalTexture");
	private static readonly int VirtualHeightTextureId = Shader.PropertyToID("VirtualHeightTexture");

	public readonly Texture2DArray albedoSmoothness, normal, height;
	public readonly ResourceHandle<RenderTexture> indirectionTexture;
	public readonly ResourceHandle<GraphicsBuffer> feedbackBuffer, virtualTextureData;

	public VirtualTextureData(Texture2DArray albedoSmoothness, Texture2DArray normal, Texture2DArray height, ResourceHandle<RenderTexture> indirectionTexture, ResourceHandle<GraphicsBuffer> feedbackBuffer, ResourceHandle<GraphicsBuffer> virtualTextureData)
	{
		this.albedoSmoothness = albedoSmoothness;
		this.normal = normal;
		this.height = height;
		this.indirectionTexture = indirectionTexture;
		this.feedbackBuffer = feedbackBuffer;
		this.virtualTextureData = virtualTextureData;
	}

	void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadTexture("IndirectionTexture", indirectionTexture);
		pass.WriteBuffer("VirtualFeedbackTexture", feedbackBuffer);
		pass.ReadBuffer("VirtualFeedbackTexture", feedbackBuffer);
		pass.ReadBuffer("VirtualTextureData", virtualTextureData);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetTexture(VirtualTextureId, albedoSmoothness);
		pass.SetTexture(VirtualNormalTextureId, normal);
		pass.SetTexture(VirtualHeightTextureId, height);
		command.SetRandomWriteTarget(4, pass.GetBuffer(feedbackBuffer));
	}
}
