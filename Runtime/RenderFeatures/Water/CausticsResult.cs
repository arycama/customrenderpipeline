using UnityEngine;
using UnityEngine.Rendering;

public struct CausticsResult : IRenderPassData
{
    private readonly ResourceHandle<RenderTexture> caustics;
    private readonly int cascade;
    private readonly float depth;

    public CausticsResult(ResourceHandle<RenderTexture> caustics, int cascade, float depth)
    {
        this.caustics = caustics;
        this.cascade = cascade;
        this.depth = depth;
    }

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
        pass.ReadTexture("OceanCaustics", caustics);
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
		pass.SetFloat("CausticsCascade", cascade);
		pass.SetFloat("CausticsDepth", depth);
	}
}
