using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class TerrainScreenspaceRenderer : RenderFeature
    {
        private readonly Material material;

        public TerrainScreenspaceRenderer(RenderGraph renderGraph) : base(renderGraph)
        {
            material = new Material(Shader.Find("Hidden/Screen Space Terrain")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public override void Render()
        {
            if (!renderGraph.IsRenderPassDataValid<TerrainRenderData>())
                return;

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Terrain Screen Pass"))
            {
                pass.Initialize(material);
                pass.WriteDepth(renderGraph.GetResource<CameraDepthData>(), RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(renderGraph.GetResource<AlbedoMetallicData>());
                pass.WriteTexture(renderGraph.GetResource<NormalRoughnessData>());
                pass.WriteTexture(renderGraph.GetResource<BentNormalOcclusionData>());

                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<TerrainRenderData>();
                pass.AddRenderPassData<CameraDepthData>();
            }
        }
    }
}