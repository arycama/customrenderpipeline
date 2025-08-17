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
	public bool ZClip { get; }

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

	public void SetInputs(RenderPassBase pass)
	{
		pass.ReadBuffer("PerCascadeData", perCascadeData);
	}

	public void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}
