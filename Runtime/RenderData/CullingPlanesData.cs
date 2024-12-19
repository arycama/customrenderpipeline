using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class CullingPlanesData : IRenderPassData
    {
        public CullingPlanes CullingPlanes { get; }

        public CullingPlanesData(CullingPlanes cullingPlanes) => CullingPlanes = cullingPlanes;

        public void SetInputs(RenderPass pass)
        {
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}