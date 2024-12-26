﻿using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class TerrainSystemData : IRenderPassData
    {
        public RTHandle MinMaxHeights { get; }
        public Terrain Terrain { get; }
        public TerrainData TerrainData { get; }
        public BufferHandle IndexBuffer { get; }

        public TerrainSystemData(RTHandle minMaxHeights, Terrain terrain, TerrainData terrainData, BufferHandle indexBuffer)
        {
            MinMaxHeights = minMaxHeights ?? throw new ArgumentNullException(nameof(minMaxHeights));
            Terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            TerrainData = terrainData ?? throw new ArgumentNullException(nameof(terrainData));
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