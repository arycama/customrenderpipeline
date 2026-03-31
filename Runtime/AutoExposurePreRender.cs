using System.Collections.Generic;
using UnityEngine;

public class AutoExposurePreRender : ViewRenderFeature
{
	private readonly Dictionary<int, ResourceHandle<GraphicsBuffer>> exposureBuffers = new();
	private readonly ColorGrading.Settings settings;
	private readonly LensSettings lensSettings;
    private readonly AutoExposure.Settings autoExposureSettings;

	public AutoExposurePreRender(RenderGraph renderGraph, ColorGrading.Settings settings, LensSettings lensSettings, AutoExposure.Settings autoExposureSettings) : base(renderGraph)
	{
		this.settings = settings;
        this.lensSettings = lensSettings;
        this.autoExposureSettings = autoExposureSettings;
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

        using (var pass = renderGraph.AddGenericRenderPass("Auto Exposure", (exposureBuffer, lensSettings, autoExposureSettings.ExposureCompensation)))
        {
            if (isFirst)
            {
                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    var initialEv100 = PhysicalCameraUtility.ComputeEV100(data.lensSettings.Aperture, data.lensSettings.ShutterSpeed, data.lensSettings.Iso) - data.ExposureCompensation;
                    var initialExposure = PhysicalCameraUtility.EV100ToExposure(initialEv100);

                    var initialData = ArrayPool<Vector4>.Get(1);
                    initialData[0] = new Vector4(initialExposure, Math.Rcp(initialExposure), 1.0f, data.ExposureCompensation);
                    command.SetBufferData(pass.GetBuffer(data.exposureBuffer), initialData);
                    ArrayPool<Vector4>.Release(initialData);
                });
            }

            renderGraph.SetResource(new AutoExposureData(exposureBuffer, isFirst, settings.PaperWhite));
        }
	}
}