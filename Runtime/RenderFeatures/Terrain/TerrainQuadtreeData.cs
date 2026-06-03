using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    public readonly struct TerrainQuadtreeData : IRenderPassData
    {
        private readonly ResourceHandle<GraphicsBuffer> terrainQuadtreeData;

        public TerrainQuadtreeData(ResourceHandle<GraphicsBuffer> terrainQuadtreeData) => this.terrainQuadtreeData = terrainQuadtreeData;

        void IRenderPassData.SetInputs(RenderPass pass)
        {
            pass.ReadBuffer("TerrainQuadtreeData", terrainQuadtreeData);
        }

        void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}