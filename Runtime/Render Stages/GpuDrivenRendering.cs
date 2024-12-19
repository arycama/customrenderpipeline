using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class GpuDrivenRendering : RenderFeature
    {
        public GpuDrivenRendering(RenderGraph renderGraph) : base(renderGraph)
        {
        }

        public override void Render()
        {
        }
    }

    public struct InstanceRendererData
    {
        public GameObject[] GameObjects { get; private set; }
        public ComputeBuffer PositionBuffer { get; private set; }
        public ComputeBuffer InstanceTypeIdBuffer { get; private set; }
        public int Count { get; set; }

        public InstanceRendererData(ComputeBuffer positionBuffer, ComputeBuffer instanceTypeIdBuffer, GameObject[] gameObjects, int count)
        {
            GameObjects = gameObjects;
            PositionBuffer = positionBuffer;
            InstanceTypeIdBuffer = instanceTypeIdBuffer;
            Count = count;
        }

        public void Clear()
        {
            if (PositionBuffer != null)
            {
                PositionBuffer.Release();
                PositionBuffer = null;
            }

            if (InstanceTypeIdBuffer != null)
            {
                InstanceTypeIdBuffer.Release();
                InstanceTypeIdBuffer = null;
            }
        }
    }
}