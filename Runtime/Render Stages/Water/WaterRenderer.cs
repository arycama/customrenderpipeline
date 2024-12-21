using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public class WaterRenderer : RenderFeature
    {
        private readonly WaterSystem waterSystem;

        public WaterRenderer(RenderGraph renderGraph , WaterSystem waterSystem) : base(renderGraph)
        {
            this.waterSystem = waterSystem;
        }

        public override void Render()
        {
            var Settings = waterSystem.Settings;
            if (!Settings.IsEnabled)
                return;

            var passData = waterSystem.Cull(renderGraph.GetResource<ViewData>().ViewPosition, renderGraph.GetResource<CullingPlanesData>().CullingPlanes);
            var viewData = renderGraph.GetResource<ViewData>();

            // Writes (worldPos - displacementPos).xz. Uv coord is reconstructed later from delta and worldPosition (reconstructed from depth)
            var oceanRenderResult = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.R16G16_SFloat, isScreenTexture: true);

            // Also write triangleNormal to another texture with oct encoding. This allows reconstructing the derivative correctly to avoid mip issues on edges,
            // As well as backfacing triangle detection for rendering under the surface
            var waterTriangleNormal = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.R16G16_UNorm, isScreenTexture: true);

            var passIndex = Settings.Material.FindPass("Water");
            Assert.IsTrue(passIndex != -1, "Water Material has no Water Pass");

            var profile = Settings.Profile;
            var resolution = Settings.Resolution;

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Ocean Clear Pass"))
            {
                pass.SetRenderFunction((command, pass) =>
                {
                    command.SetRenderTarget(waterTriangleNormal);
                    command.ClearRenderTarget(false, true, Color.clear);
                });
            }

            using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Ocean Render"))
            {
                pass.Initialize(Settings.Material, waterSystem.IndexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);

                pass.WriteDepth(renderGraph.GetResource<CameraDepthData>().Handle);
                pass.WriteTexture(oceanRenderResult, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(renderGraph.GetResource<VelocityData>().Handle);
                pass.WriteTexture(waterTriangleNormal, RenderBufferLoadAction.DontCare);

                pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);

                pass.AddRenderPassData<OceanFftResult>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<WaterShoreMask.Result>();
                pass.AddRenderPassData<ICommonPassData>();

                var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().CullingPlanes;

                pass.SetRenderFunction((System.Action<CommandBuffer, RenderPass>)((command, pass) =>
                {
                    pass.SetInt("_VerticesPerEdge", waterSystem.VerticesPerTileEdge);
                    pass.SetInt("_VerticesPerEdgeMinusOne", waterSystem.VerticesPerTileEdge - 1);
                    pass.SetFloat("_RcpVerticesPerEdgeMinusOne", 1f / (waterSystem.VerticesPerTileEdge - 1));
                    pass.SetInt("_OceanTextureSlicePreviousOffset", ((renderGraph.FrameIndex & 1) == 0) ? 0 : 4);

                    // Snap to quad-sized increments on largest cell
                    var texelSize = Settings.Size / (float)Settings.PatchVertices;
                    var positionX = MathUtils.Snap(viewData.ViewPosition.x, texelSize) - viewData.ViewPosition.x - Settings.Size * 0.5f;
                    var positionZ = MathUtils.Snap(viewData.ViewPosition.z, texelSize) - viewData.ViewPosition.z - Settings.Size * 0.5f;
                    pass.SetVector("_PatchScaleOffset", new Vector4(Settings.Size / (float)Settings.CellCount, Settings.Size / (float)Settings.CellCount, positionX, positionZ));

                    pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
                    var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
                    for (var i = 0; i < cullingPlanes.Count; i++)
                        cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

                    pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                    ArrayPool<Vector4>.Release(cullingPlanesArray);

                    pass.SetFloat("_ShoreWaveWindSpeed", Settings.Profile.WindSpeed);
                    pass.SetFloat("_ShoreWaveWindAngle", Settings.Profile.WindAngle);
                }));
            }

            renderGraph.SetResource(new WaterPrepassResult(oceanRenderResult, waterTriangleNormal, (Vector4)Settings.Material.GetColor("_Color").linear, (Vector4)Settings.Material.GetColor("_Extinction")));
        }
    }
}