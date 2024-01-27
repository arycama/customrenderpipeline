using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class CameraMotionVectors : RenderFeature
    {
        private readonly Material material;

        public CameraMotionVectors(RenderGraph renderGraph) : base(renderGraph)
        {
            material = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Render(RTHandle motionVectors, RTHandle cameraDepth)
        {
            using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Camera Motion Vectors");
            pass.Material = material;
            pass.Index = 0;

            pass.ReadTexture("_CameraDepth", cameraDepth);
            pass.WriteTexture("", motionVectors, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            pass.WriteDepth("", cameraDepth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, 1.0f, RenderTargetFlags.ReadOnlyDepthStencil);
        }
    }
}