using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class TerrainSystemData : IRenderPassData
    {
        public ResourceHandle<RenderTexture> MinMaxHeights { get; }
        public Terrain Terrain { get; }
        public TerrainData TerrainData { get; }
        public ResourceHandle<GraphicsBuffer> IndexBuffer { get; }

        public TerrainSystemData(ResourceHandle<RenderTexture> minMaxHeights, Terrain terrain, TerrainData terrainData, ResourceHandle<GraphicsBuffer> indexBuffer)
        {
            MinMaxHeights = minMaxHeights;
            Terrain = terrain;
            TerrainData = terrainData;
            IndexBuffer = indexBuffer;
        }

        void IRenderPassData.SetInputs(RenderPass pass)
        {
            pass.ReadBuffer("", IndexBuffer);
        }

        void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}