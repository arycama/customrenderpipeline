using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    public class DecalComposite : ViewRenderFeature
    {
        private readonly Material material;

        public DecalComposite(RenderGraph renderGraph) : base(renderGraph)
        {
            material = new Material(Shader.Find("Hidden/Decal Composite")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
        {
            using var scope = renderGraph.AddProfileScope("Decal Composite");

            var albedoMetallicCopy = renderGraph.GetTexture(renderGraph.GetRtHandleData<GBufferAlbedoMetallic>().handle);
            var normalRoughnessCopy = renderGraph.GetTexture(renderGraph.GetRtHandleData<GBufferNormalRoughness>().handle);
            var bentNormalOcclusionCopy = renderGraph.GetTexture(renderGraph.GetRtHandleData<GBufferBentNormalOcclusion>().handle);

            // Copy the existing gbuffer textures to new ones
            // TODO: Would a direct copy be faster?
            using (var pass = renderGraph.AddFullscreenRenderPass("Copy"))
            {
                pass.Initialize(material, viewPassData.viewSize, 1, 0, isScreenPass: true);
                pass.PreventNewSubPass = true;

                pass.WriteRtHandleDepth<CameraDepth>(SubPassFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(albedoMetallicCopy);
                pass.WriteTexture(normalRoughnessCopy);
                pass.WriteTexture(bentNormalOcclusionCopy);

                pass.ReadRtHandle<GBufferAlbedoMetallic>();
                pass.ReadRtHandle<GBufferNormalRoughness>();
                pass.ReadRtHandle<GBufferBentNormalOcclusion>();
            }

            // Now composite the decal buffers
            using (var pass = renderGraph.AddFullscreenRenderPass("Combine", (albedoMetallicCopy, normalRoughnessCopy, bentNormalOcclusionCopy)))
            {
                pass.Initialize(material, viewPassData.viewSize, 1, 1, isScreenPass: true);
                pass.PreventNewSubPass = true;

                pass.WriteRtHandleDepth<CameraDepth>(SubPassFlags.ReadOnlyDepthStencil);
                pass.WriteRtHandle<GBufferAlbedoMetallic>();
                pass.WriteRtHandle<GBufferNormalRoughness>();
                pass.WriteRtHandle<GBufferBentNormalOcclusion>();

                pass.ReadTexture("AlbedoMetallicCopy", albedoMetallicCopy);
                pass.ReadTexture("NormalRoughnessCopy", normalRoughnessCopy);
                pass.ReadTexture("BentNormalOcclusionCopy", bentNormalOcclusionCopy);

                pass.ReadRtHandle<CameraTarget>();
                pass.ReadRtHandle<CameraDepth>();
                pass.ReadRtHandle<DecalAlbedo>();
                pass.ReadRtHandle<DecalNormal>();

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    pass.SetVector("AlbedoMetallicCopyScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.albedoMetallicCopy));
                    pass.SetVector("NormalRoughnessCopyScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.normalRoughnessCopy));
                    pass.SetVector("BentNormalOcclusionCopyScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.bentNormalOcclusionCopy));
                });
            }
        }
    }
}