using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class CameraMotionVectors : RenderFeature<(RTHandle velocity, RTHandle cameraDepth, int width, int height)>
    {
        private readonly Material material;

        public CameraMotionVectors(RenderGraph renderGraph) : base(renderGraph)
        {
            material = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public override void Render((RTHandle velocity, RTHandle cameraDepth, int width, int height) data)
        {
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Camera Velocity"))
            {
                pass.Initialize(material);
                pass.ReadTexture("Depth", data.cameraDepth);
                pass.WriteTexture(data.velocity);
                pass.WriteDepth(data.cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<TemporalAAData>();
            }

            var result = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.R16G16_SFloat);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Dilate Velocity"))
            {
                pass.Initialize(material, 1);
                pass.ReadTexture("Depth", data.cameraDepth);
                pass.ReadTexture("Velocity", data.velocity);
                pass.WriteTexture(result, RenderBufferLoadAction.DontCare);
                pass.AddRenderPassData<ICommonPassData>();
            }

            renderGraph.SetResource<VelocityData>(new VelocityData(result));;
        }
    }
}