using System;
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

        var camera = viewRenderData.camera;
        var iso = camera.usePhysicalProperties ? camera.iso : lensSettings.Iso;
        var aperture = camera.usePhysicalProperties ? camera.aperture : lensSettings.Aperture;
        var shutterSpeed = camera.usePhysicalProperties ? 1.0f / camera.shutterSpeed : lensSettings.ShutterSpeed;

        using (var pass = renderGraph.AddGenericRenderPass("Auto Exposure", (exposureBuffer, iso, aperture, shutterSpeed, autoExposureSettings.ExposureCompensation)))
        {
            if (isFirst)
            {
                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    var initialEv100 = PhysicalCameraUtility.ComputeEV100(data.aperture, data.shutterSpeed, data.iso) - data.ExposureCompensation;
                    var initialExposure = PhysicalCameraUtility.EV100ToExposure(initialEv100);

                    Span<Float4> initialData = stackalloc Float4[1];
                    initialData[0] = new Float4(initialExposure, Math.Rcp(initialExposure), 1.0f, data.ExposureCompensation);
                    command.SetBufferData(pass.GetBuffer(data.exposureBuffer), initialData);
                });
            }

            renderGraph.SetResource(new AutoExposureData(exposureBuffer, isFirst, settings.PaperWhite));
        }
	}
}