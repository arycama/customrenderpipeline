using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    public struct GpuDrivenRenderingData : IRenderPassData
    {
        public ResourceHandle<GraphicsBuffer> positions, instanceTypes, lodFades, lodSizes, instanceTypeData, instanceTypeLodData, drawCallArgs, instanceBounds, rendererLodIndices;
        public Dictionary<string, List<RendererDrawCallData>> rendererDrawCallData;
        public int instanceCount, rendererCount, lodCount;

        public GpuDrivenRenderingData(ResourceHandle<GraphicsBuffer> positions, ResourceHandle<GraphicsBuffer> instanceTypes, ResourceHandle<GraphicsBuffer> lodFades, ResourceHandle<GraphicsBuffer> lodSizes, ResourceHandle<GraphicsBuffer> instanceTypeData, ResourceHandle<GraphicsBuffer> instanceTypeLodData, ResourceHandle<GraphicsBuffer> drawCallArgs, ResourceHandle<GraphicsBuffer> instanceBounds, ResourceHandle<GraphicsBuffer> rendererLodIndices, Dictionary<string, List<RendererDrawCallData>> rendererDrawCallData, int instanceCount, int rendererCount, int lodCount)
        {
            this.positions = positions;
            this.instanceTypes = instanceTypes;
            this.lodFades = lodFades;
            this.lodSizes = lodSizes;
            this.instanceTypeData = instanceTypeData;
            this.instanceTypeLodData = instanceTypeLodData;
            this.drawCallArgs = drawCallArgs;
            this.rendererDrawCallData = rendererDrawCallData;
            this.instanceCount = instanceCount;
            this.instanceBounds = instanceBounds;
            this.rendererCount = rendererCount;
            this.lodCount = lodCount;
            this.rendererLodIndices = rendererLodIndices;
        }

        void IRenderPassData.SetInputs(RenderPass pass)
        {
        }

        void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}