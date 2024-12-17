using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class CullingResultsData : IRenderPassData
    {
        public CullingResults CullingResults { get; }

        public CullingResultsData(CullingResults cullingResults)
        {
            CullingResults = cullingResults;
        }

        public void SetInputs(RenderPass pass)
        {
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}