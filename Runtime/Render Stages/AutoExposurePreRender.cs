using System.Collections.Generic;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class AutoExposurePreRender : RenderFeature
    {
        private readonly Dictionary<int, BufferHandle> exposureBuffers = new();

        public AutoExposurePreRender(RenderGraph renderGraph) : base(renderGraph)
        {
        }

        protected override void Cleanup(bool disposing)
        {
            foreach(var buffer in exposureBuffers.Values)
            {
                renderGraph.ReleasePersistentResource(buffer);
            }
        }

        public override void Render()
        {
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Auto Exposure"))
            {
                var viewData = renderGraph.GetResource<ViewData>();
                var isFirst = !exposureBuffers.TryGetValue(viewData.ViewIndex, out var exposureBuffer);
                if (isFirst)
                {
                    exposureBuffer = renderGraph.GetBuffer(1, sizeof(float) * 4, GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination, GraphicsBuffer.UsageFlags.None, true);
                    exposureBuffers.Add(viewData.ViewIndex, exposureBuffer);
                }

                // For first pass, set to 1.0f 
                if (isFirst)
                {
                    pass.SetRenderFunction(exposureBuffer, (command, pass, data) =>
                    {
                        var initialData = ArrayPool<Vector4>.Get(1);
                        initialData[0] = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                        command.SetBufferData(pass.GetBuffer(data), initialData);
                        ArrayPool<Vector4>.Release(initialData);
                    });
                }

                renderGraph.SetResource(new AutoExposureData(exposureBuffer, isFirst)); ;
            }
        }
    }
}