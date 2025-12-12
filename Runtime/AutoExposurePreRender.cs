using System.Collections.Generic;
using UnityEngine;

public class AutoExposurePreRender : ViewRenderFeature
{
	private readonly Dictionary<int, ResourceHandle<GraphicsBuffer>> exposureBuffers = new();
	private Tonemapping.Settings settings;

	public AutoExposurePreRender(RenderGraph renderGraph, Tonemapping.Settings settings) : base(renderGraph)
	{
		this.settings = settings;
	}

	protected override void Cleanup(bool disposing)
	{
		foreach (var buffer in exposureBuffers.Values)
		{
			renderGraph.ReleasePersistentResource(buffer, -1);
		}
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		var isFirst = !exposureBuffers.TryGetValue(viewRenderData.viewId, out var exposureBuffer);
		if (isFirst)
		{
			exposureBuffer = renderGraph.GetBuffer(1, sizeof(float) * 4, GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination, GraphicsBuffer.UsageFlags.None, true);
			exposureBuffers.Add(viewRenderData.viewId, exposureBuffer);
		}

		using (var pass = renderGraph.AddGenericRenderPass("Auto Exposure", exposureBuffer))
		{
			// For first pass, set to 1.0f 
			if (isFirst)
			{
				pass.SetRenderFunction(static (command, pass, data) =>
				{
					var initialData = ArrayPool<Vector4>.Get(1);
					initialData[0] = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
					command.SetBufferData(pass.GetBuffer(data), initialData);
					ArrayPool<Vector4>.Release(initialData);
				});
			}

			renderGraph.SetResource(new AutoExposureData(exposureBuffer, isFirst, settings.PaperWhite));
		}
	}
}