using UnityEngine;
using UnityEngine.Experimental.Rendering;
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

        public override void Render()
        {
            var viewData = renderGraph.GetResource<ViewData>();
            var depth = renderGraph.GetResource<CameraDepthData>().Handle;
            var velocity = renderGraph.GetResource<VelocityData>().Handle;

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Camera Velocity"))
            {
                pass.Initialize(material);
                pass.ReadTexture("Depth", depth);
                pass.WriteTexture(velocity);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<TemporalAAData>();
            }

            var result = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.R16G16_SFloat);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Dilate Velocity"))
            {
                pass.Initialize(material, 1);
                pass.ReadTexture("Depth", depth);
                pass.ReadTexture("Velocity", velocity);
                pass.WriteTexture(result, RenderBufferLoadAction.DontCare);
                pass.AddRenderPassData<ICommonPassData>();
            }

            renderGraph.SetResource(new VelocityData(result));
        }
    }
}