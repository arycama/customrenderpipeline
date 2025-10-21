using UnityEngine;
using UnityEngine.Rendering;

public readonly struct ShadowRequestData : IRenderPassData
{
	public readonly ShadowRequest ShadowRequest;
	public readonly float Bias;
	public readonly float SlopeBias;
	public readonly ResourceHandle<RenderTexture> Shadow;
	public readonly int CascadeIndex;
	public readonly ResourceHandle<GraphicsBuffer> perCascadeData;
	public readonly bool ZClip;

	public ShadowRequestData(ShadowRequest shadowRequest, float bias, float slopBias, ResourceHandle<RenderTexture> shadow, int cascadeIndex, ResourceHandle<GraphicsBuffer> perCascadeData, bool zClip)
	{
		ShadowRequest = shadowRequest;
		Bias = bias;
		SlopeBias = slopBias;
		Shadow = shadow;
		CascadeIndex = cascadeIndex;
		this.perCascadeData = perCascadeData;
		ZClip = zClip;
	}

	public void SetInputs(RenderPass pass)
	{
		pass.ReadBuffer("PerCascadeData", perCascadeData);
	}

	public void SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}
