using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public struct GpuInstanceBuffersData : IRenderPassData
    {
        public ResourceHandle<GraphicsBuffer> positionsBuffer, instanceTypeIdsBuffer, lodFadesBuffer, rendererBoundsBuffer, lodSizesBuffer, instanceTypeDataBuffer, instanceTypeLodDataBuffer, submeshOffsetLengthsBuffer, drawCallArgsBuffer;
        public Dictionary<string, List<RendererDrawCallData>> rendererDrawCallData;
        public int totalInstanceCount, instanceTimesRendererCount, totalRendererSum;

        public GpuInstanceBuffersData(ResourceHandle<GraphicsBuffer> positionsBuffer, ResourceHandle<GraphicsBuffer> instanceTypeIdsBuffer, ResourceHandle<GraphicsBuffer> lodFadesBuffer, ResourceHandle<GraphicsBuffer> rendererBoundsBuffer, ResourceHandle<GraphicsBuffer> lodSizesBuffer, ResourceHandle<GraphicsBuffer> instanceTypeDataBuffer, ResourceHandle<GraphicsBuffer> instanceTypeLodDataBuffer, ResourceHandle<GraphicsBuffer> submeshOffsetLengthsBuffer, ResourceHandle<GraphicsBuffer> drawCallArgsBuffer, Dictionary<string, List<RendererDrawCallData>> rendererDrawCallData, int totalInstanceCount, int instanceTimesRendererCount, int totalRendererSum)
        {
            this.positionsBuffer = positionsBuffer;
            this.instanceTypeIdsBuffer = instanceTypeIdsBuffer;
            this.lodFadesBuffer = lodFadesBuffer;
            this.rendererBoundsBuffer = rendererBoundsBuffer;
            this.lodSizesBuffer = lodSizesBuffer;
            this.instanceTypeDataBuffer = instanceTypeDataBuffer;
            this.instanceTypeLodDataBuffer = instanceTypeLodDataBuffer;
            this.submeshOffsetLengthsBuffer = submeshOffsetLengthsBuffer;
            this.drawCallArgsBuffer = drawCallArgsBuffer;
            this.rendererDrawCallData = rendererDrawCallData;
            this.totalInstanceCount = totalInstanceCount;
            this.instanceTimesRendererCount = instanceTimesRendererCount;
            this.totalRendererSum = totalRendererSum;
        }

        void IRenderPassData.SetInputs(RenderPass pass)
        {
        }

        void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}