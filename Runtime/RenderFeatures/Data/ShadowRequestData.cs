using UnityEngine;
using UnityEngine.Rendering;

public class ShadowRequestData : IRenderPassData
{
	public ShadowRequest ShadowRequest { get; }
	public float Bias { get; }
	public float SlopeBias { get; }
	public ResourceHandle<RenderTexture> Shadow { get; }
	public int CascadeIndex { get; }
	ResourceHandle<GraphicsBuffer> perCascadeData;

	public ShadowRequestData(ShadowRequest shadowRequest, float bias, float slopBias, ResourceHandle<RenderTexture> shadow, int cascadeIndex, ResourceHandle<GraphicsBuffer> perCascadeData)
	{
		ShadowRequest = shadowRequest;
		Bias = bias;
		SlopeBias = slopBias;
		Shadow = shadow;
		CascadeIndex = cascadeIndex;
		this.perCascadeData = perCascadeData;
	}

	public void SetInputs(RenderPassBase pass)
	{
		pass.ReadBuffer("PerCascadeData", perCascadeData);
	}

	public void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}
