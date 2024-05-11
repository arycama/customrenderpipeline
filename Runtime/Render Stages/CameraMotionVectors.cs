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

        public RTHandle Render(RTHandle velocity, RTHandle cameraDepth, int width, int height, Camera camera, ICommonPassData commonPassData)
        {
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Camera Velocity"))
            {
                pass.Initialize(material, camera: camera);
                pass.ReadTexture("Depth", cameraDepth);
                pass.WriteTexture(velocity);
                pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);
                });
            }

            var result = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16_SFloat);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Dilate Velocity"))
            {
                pass.Initialize(material, 1, camera: camera);
                pass.ReadTexture("Depth", cameraDepth);
                pass.ReadTexture("Velocity", velocity);
                pass.WriteTexture(result, RenderBufferLoadAction.DontCare);
                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);
                });
            }

            return result;
        }

        private class PassData
        {
        }
    }
}