using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public struct RendererDrawCallData
    {
        public int renderQueue;
        public Mesh mesh;
        public int submeshIndex;
        public Material material;
        public int passIndex;
        public int indirectArgsOffset;
        public int rendererOffset;

        public RendererDrawCallData(int renderQueue, Mesh mesh, int submeshIndex, Material material, int passIndex, int indirectArgsOffset, int rendererOffset)
        {
            this.renderQueue = renderQueue;
            this.mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
            this.submeshIndex = submeshIndex;
            this.material = material ?? throw new ArgumentNullException(nameof(material));
            this.passIndex = passIndex;
            this.indirectArgsOffset = indirectArgsOffset;
            this.rendererOffset = rendererOffset;
        }
    }
}