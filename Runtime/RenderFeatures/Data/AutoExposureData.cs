using UnityEngine;
using UnityEngine.Rendering;

public readonly struct AutoExposureData : IRenderPassData
{
	public ResourceHandle<GraphicsBuffer> ExposureBuffer { get; }
	public bool IsFirst { get; }
    private readonly float paperWhite;

	public AutoExposureData(ResourceHandle<GraphicsBuffer> exposureBuffer, bool isFirst, float maxLuminance)
	{
		this.ExposureBuffer = exposureBuffer;
		IsFirst = isFirst;
        this.paperWhite = maxLuminance;
    }

	public readonly void SetInputs(RenderPassBase pass)
	{
		pass.ReadBuffer("Exposure", ExposureBuffer);
	}

	public readonly void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
        pass.SetFloat("PaperWhite", paperWhite);
	}
}