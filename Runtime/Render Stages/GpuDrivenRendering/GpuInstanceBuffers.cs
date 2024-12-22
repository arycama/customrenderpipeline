using System.Collections.Generic;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public struct GpuInstanceBuffers
    {
        public ComputeBuffer rendererInstanceIDsBuffer, rendererInstanceIndexOffsetsBuffer, rendererCountsBuffer, finalRendererCountsBuffer, visibleRendererInstanceIndicesBuffer, positionsBuffer, instanceTypeIdsBuffer, lodFadesBuffer, rendererBoundsBuffer, lodSizesBuffer, instanceTypeDataBuffer, instanceTypeLodDataBuffer, submeshOffsetLengthsBuffer, drawCallArgsBuffer;
        public List<InstanceRendererData> readyInstanceData;
        public Dictionary<string, List<RendererDrawCallData>> rendererDrawCallData;

        public GpuInstanceBuffers(ComputeBuffer rendererInstanceIDsBuffer, ComputeBuffer rendererInstanceIndexOffsetsBuffer, ComputeBuffer rendererCountsBuffer, ComputeBuffer finalRendererCountsBuffer, ComputeBuffer visibleRendererInstanceIndicesBuffer, ComputeBuffer positionsBuffer, ComputeBuffer instanceTypeIdsBuffer, ComputeBuffer lodFadesBuffer, ComputeBuffer rendererBoundsBuffer, ComputeBuffer lodSizesBuffer, ComputeBuffer instanceTypeDataBuffer, ComputeBuffer instanceTypeLodDataBuffer, ComputeBuffer submeshOffsetLengthsBuffer, ComputeBuffer drawCallArgsBuffer, List<InstanceRendererData> readyInstanceData, Dictionary<string, List<RendererDrawCallData>> rendererDrawCallData)
        {
            this.rendererInstanceIDsBuffer = rendererInstanceIDsBuffer;
            this.rendererInstanceIndexOffsetsBuffer = rendererInstanceIndexOffsetsBuffer;
            this.rendererCountsBuffer = rendererCountsBuffer;
            this.finalRendererCountsBuffer = finalRendererCountsBuffer;
            this.visibleRendererInstanceIndicesBuffer = visibleRendererInstanceIndicesBuffer;
            this.positionsBuffer = positionsBuffer;
            this.instanceTypeIdsBuffer = instanceTypeIdsBuffer;
            this.lodFadesBuffer = lodFadesBuffer;
            this.rendererBoundsBuffer = rendererBoundsBuffer;
            this.lodSizesBuffer = lodSizesBuffer;
            this.instanceTypeDataBuffer = instanceTypeDataBuffer;
            this.instanceTypeLodDataBuffer = instanceTypeLodDataBuffer;
            this.submeshOffsetLengthsBuffer = submeshOffsetLengthsBuffer;
            this.drawCallArgsBuffer = drawCallArgsBuffer;
            this.readyInstanceData = readyInstanceData;
            this.rendererDrawCallData = rendererDrawCallData;
        }
    }
}