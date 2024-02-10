using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class PhysicalSky
    {
        [Serializable]
        public class Settings
        {

        }

        private RenderGraph renderGraph;
        private Settings settings;
        private Material material;

        public PhysicalSky(RenderGraph renderGraph, Settings settings)
        {
            this.renderGraph = renderGraph;
            this.settings = settings;

            material = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Render(RTHandle target, RTHandle depth, BufferHandle exposureBuffer, int width, int height, float fov, float aspect, Matrix4x4 viewToWorld, Vector3 viewPosition, LightingSetup.Result lightingSetupResult)
        {
            using(var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky"))
            {
                pass.Material = material;
                pass.Index = 0;

                pass.WriteTexture("", target, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                pass.WriteDepth("", depth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, flags: RenderTargetFlags.ReadOnlyDepthStencil);

                lightingSetupResult.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    lightingSetupResult.SetProperties(pass, command);

                    pass.SetVector(command, "_ViewPosition", viewPosition);
                    pass.SetConstantBuffer(command, "Exposure", exposureBuffer);
                    pass.SetMatrix(command, "_PixelCoordToViewDirWS", Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(width, height, fov, aspect, viewToWorld));
                });
            }
        }

        public void Cleanup()
        {
        }

        private class PassData
        {
        }
    }
}
