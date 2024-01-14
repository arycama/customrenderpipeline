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
            var pass = renderGraph.AddRenderPass(new FullscreenRenderPass(material));
            pass.ReadTexture("_CameraDepth", cameraDepth);

            pass.SetRenderFunction((command, context) =>
            {
                using var profilerScope = command.BeginScopedSample("Camera Motion Vectors");

                command.SetRenderTarget(new RenderTargetBinding(motionVectors, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, cameraDepth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store) { flags = RenderTargetFlags.ReadOnlyDepthStencil });

                pass.Execute(command);
            });
        }
    }
}