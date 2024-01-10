using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public struct ScopedPropertyBlock : IDisposable
    {
        private RenderGraph renderGraph;

        public ScopedPropertyBlock(RenderGraph renderGraph)
        {
            this.renderGraph = renderGraph;
            PropertyBlock = renderGraph.GetPropertyBlock();
        }

        public MaterialPropertyBlock PropertyBlock { get; }

        void IDisposable.Dispose()
        {
            renderGraph.ReleasePropertyBlock(PropertyBlock);
        }

        public void SetFloat(string name, float value) => PropertyBlock.SetFloat(name, value);
        public void SetInt(string name, int value) => PropertyBlock.SetInt(name, value);
        public void SetTexture(string name, Texture texture) => PropertyBlock.SetTexture(name, texture);
        public void SetVector(string name, Vector4 value) => PropertyBlock.SetVector(name, value);

        public static implicit operator MaterialPropertyBlock(ScopedPropertyBlock scopedPropertyBlock) => scopedPropertyBlock.PropertyBlock;
    }
}