using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unmath;
using static Unmath.Math;

namespace CustomRenderPipeline
{
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

        public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
        {
            var isFirst = !exposureBuffers.TryGetValue(viewPassData.viewId, out var exposureBuffer);
            if (isFirst)
            {
                exposureBuffer = renderGraph.GetBuffer(1, sizeof(float) * 4, GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination, GraphicsBuffer.UsageFlags.None, true);
                exposureBuffers.Add(viewPassData.viewId, exposureBuffer);
            }

            using (var pass = renderGraph.AddGenericRenderPass("Auto Exposure", (exposureBuffer, viewPassData.exposure, autoExposureSettings.ExposureCompensation)))
            {
                if (isFirst)
                {
                    pass.SetRenderFunction(static (command, pass, data) =>
                    {
                        Span<Float4> initialData = stackalloc Float4[1];
                        initialData[0] = new Float4(data.exposure, Rcp(data.exposure), 1.0f, data.ExposureCompensation);
                        command.SetBufferData(pass.GetBuffer(data.exposureBuffer), initialData);
                    });
                }

                renderGraph.SetResource(new AutoExposureData(exposureBuffer, isFirst, settings.PaperWhite));
            }
        }
    }
}