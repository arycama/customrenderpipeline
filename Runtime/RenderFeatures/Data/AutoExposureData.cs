using UnityEngine;
using UnityEngine.Rendering;

public readonly struct AutoExposureData : IRenderPassData
{
	public ResourceHandle<GraphicsBuffer> ExposureBuffer { get; }
	public bool IsFirst { get; }
    private readonly float paperWhite;

	public AutoExposureData(ResourceHandle<GraphicsBuffer> exposureBuffer, bool isFirst, float maxLuminance)
	{
		ExposureBuffer = exposureBuffer;
		IsFirst = isFirst;
        paperWhite = maxLuminance;
    }

	public readonly void SetInputs(RenderPass pass)
	{
		pass.ReadBuffer("ExposureBuffer", ExposureBuffer);
	}

	public readonly void SetProperties(RenderPass pass, CommandBuffer command)
	{
        pass.SetFloat("PaperWhite", paperWhite);
	}
}