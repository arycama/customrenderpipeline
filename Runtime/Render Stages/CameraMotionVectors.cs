using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class CameraMotionVectors : RenderFeature
    {
        private readonly Material motionVectorsMaterial;

        public CameraMotionVectors(RenderGraph renderGraph) : base(renderGraph)
        {
            motionVectorsMaterial = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Render(RTHandle motionVectors, RTHandle cameraDepth)
        {
            var pass = renderGraph.AddRenderPass<FullscreenRenderPass>();
            pass.ReadTexture("_CameraDepth", cameraDepth);

            pass.SetRenderFunction((command, context) =>
            {
                using var profilerScope = command.BeginScopedSample("Camera Motion Vectors");

                command.SetRenderTarget(new RenderTargetBinding(motionVectors, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, cameraDepth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store) { flags = RenderTargetFlags.ReadOnlyDepthStencil });
                command.DrawProcedural(Matrix4x4.identity, motionVectorsMaterial, 0, MeshTopology.Triangles, 3, 1, pass.GetPropertyBlock());
            });
        }
    }
}