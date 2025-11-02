using UnityEngine;
using UnityEngine.Rendering;

public readonly struct VirtualTextureData : IRenderPassData
{
	private static readonly int VirtualTextureId = Shader.PropertyToID("VirtualTexture");
	private static readonly int VirtualNormalTextureId = Shader.PropertyToID("VirtualNormalTexture");
	private static readonly int VirtualHeightTextureId = Shader.PropertyToID("VirtualHeightTexture");

	public readonly Texture2DArray albedoSmoothness, normal, height;
	public readonly ResourceHandle<RenderTexture> indirectionTexture;
	public readonly ResourceHandle<GraphicsBuffer> feedbackBuffer;
	public readonly int indirectionTextureSize, virtualTextureSize, tileSize;
	public readonly float anisoLevel, rcpIndirectionTextureSize, log2TileSize;

	public VirtualTextureData(Texture2DArray albedoSmoothness, Texture2DArray normal, Texture2DArray height, ResourceHandle<RenderTexture> indirectionTexture, ResourceHandle<GraphicsBuffer> feedbackBuffer, float anisoLevel, int indirectionTextureSize, float rcpIndirectionTextureSize, int virtualTextureSize, float log2TileSize, int tileSize)
	{
		this.albedoSmoothness = albedoSmoothness;
		this.normal = normal;
		this.height = height;
		this.indirectionTexture = indirectionTexture;
		this.anisoLevel = anisoLevel;
		this.feedbackBuffer = feedbackBuffer;
		this.indirectionTextureSize = indirectionTextureSize;
		this.rcpIndirectionTextureSize = rcpIndirectionTextureSize;
		this.virtualTextureSize = virtualTextureSize;
		this.log2TileSize = log2TileSize;
		this.tileSize = tileSize;
	}

	void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadTexture("IndirectionTexture", indirectionTexture);
		pass.WriteBuffer("VirtualFeedbackTexture", feedbackBuffer);
		pass.ReadBuffer("VirtualFeedbackTexture", feedbackBuffer);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetTexture(VirtualTextureId, albedoSmoothness);
		pass.SetTexture(VirtualNormalTextureId, normal);
		pass.SetTexture(VirtualHeightTextureId, height);
		pass.SetFloat("AnisoLevel", anisoLevel);
		pass.SetFloat("IndirectionTextureSize", indirectionTextureSize);
		pass.SetInt("IndirectionTextureSizeInt", indirectionTextureSize);
		pass.SetFloat("RcpIndirectionTextureSize", rcpIndirectionTextureSize);
		pass.SetFloat("VirtualTextureSize", virtualTextureSize);
		pass.SetFloat("Log2TileSize", log2TileSize);
		pass.SetInt("VirtualTextureSizeInt", virtualTextureSize);

		pass.SetFloat("VirtualTileSize", tileSize);
		pass.SetInt("VirtualTileSizeInt", tileSize);

		command.SetRandomWriteTarget(4, pass.GetBuffer(feedbackBuffer));
	}
}
