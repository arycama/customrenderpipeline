using System.Collections.Generic;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class AutoExposurePreRender : RenderFeature
    {
        private readonly Dictionary<int, GraphicsBuffer> exposureBuffers = new();

        public AutoExposurePreRender(RenderGraph renderGraph) : base(renderGraph)
        {
        }

        protected override void Cleanup(bool disposing)
        {
            foreach (var buffer in exposureBuffers)
                buffer.Value.Dispose();
        }

        public override void Render()
        {
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Auto Exposure"))
            {
                var viewData = renderGraph.GetResource<ViewData>();
                var isFirst = !exposureBuffers.TryGetValue(viewData.ViewIndex, out var exposureBuffer);
                if (isFirst)
                {
                    exposureBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination, 4, sizeof(float)) { name = "Auto Exposure Buffer" };
                    exposureBuffers.Add(viewData.ViewIndex, exposureBuffer);
                }

                var bufferHandle = renderGraph.BufferHandleSystem.ImportBuffer(exposureBuffer);

                // For first pass, set to 1.0f 
                if (isFirst)
                {
                    pass.SetRenderFunction(bufferHandle, (command, pass, data) =>
                    {
                        var initialData = ArrayPool<Vector4>.Get(1);
                        initialData[0] = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                        command.SetBufferData(data, initialData);
                        ArrayPool<Vector4>.Release(initialData);
                    });
                }

                renderGraph.SetResource(new AutoExposureData(bufferHandle, isFirst)); ;
            }
        }
    }
}