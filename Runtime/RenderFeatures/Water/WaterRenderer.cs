using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class WaterRenderer : WaterRendererBase
{
    public WaterRenderer(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph, settings)
    {
    }

    public override void Render(Camera camera, ScriptableRenderContext context)
    {
        if (!settings.IsEnabled)
            return;

        var passData = Cull(camera.transform.position, renderGraph.GetResource<CullingPlanesData>().CullingPlanes);

        // Writes (worldPos - displacementPos).xz. Uv coord is reconstructed later from delta and worldPosition (reconstructed from depth)
        var oceanRenderResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16G16_SFloat, isScreenTexture: true);

        // Also write triangleNormal to another texture with oct encoding. This allows reconstructing the derivative correctly to avoid mip issues on edges,
        // As well as backfacing triangle detection for rendering under the surface
        var waterTriangleNormal = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16G16_UNorm, isScreenTexture: true);

        var passIndex = settings.Material.FindPass("Water");
        Assert.IsTrue(passIndex != -1, "Water Material has no Water Pass");

        var profile = settings.Profile;
        var resolution = settings.Resolution;

        using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Ocean Clear Pass"))
        {
            pass.SetRenderFunction((command, pass) =>
            {
                command.SetRenderTarget(pass.GetRenderTexture(waterTriangleNormal));
                command.ClearRenderTarget(false, true, Color.clear);
            });
        }

        using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectIndexedRenderPass>("Ocean Render"))
        {
            pass.Initialize(settings.Material, indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);

            pass.WriteDepth(renderGraph.GetResource<CameraDepthData>().Handle);
            pass.WriteTexture(oceanRenderResult, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(renderGraph.GetResource<VelocityData>().Handle);
            pass.WriteTexture(waterTriangleNormal, RenderBufferLoadAction.DontCare);

            pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);

            pass.AddRenderPassData<OceanFftResult>();
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<TemporalAAData>();
            pass.AddRenderPassData<WaterShoreMask.Result>(true);
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<FrameData>();

            var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().CullingPlanes;

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("_VerticesPerEdge", VerticesPerTileEdge);
                pass.SetInt("_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
                pass.SetFloat("_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));
                pass.SetInt("_OceanTextureSlicePreviousOffset", ((renderGraph.FrameIndex & 1) == 0) ? 0 : 4);

                // Snap to quad-sized increments on largest cell
                var texelSize = settings.Size / (float)settings.PatchVertices;
                var positionX = Math.Snap(camera.transform.position.x, texelSize) - camera.transform.position.x - settings.Size * 0.5f;
                var positionZ = Math.Snap(camera.transform.position.z, texelSize) - camera.transform.position.z - settings.Size * 0.5f;
                pass.SetVector("_PatchScaleOffset", new Vector4(settings.Size / (float)settings.CellCount, settings.Size / (float)settings.CellCount, positionX, positionZ));

                pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
                var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
                for (var i = 0; i < cullingPlanes.Count; i++)
                    cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

                pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                ArrayPool<Vector4>.Release(cullingPlanesArray);

                pass.SetFloat("_ShoreWaveWindSpeed", settings.Profile.WindSpeed);
                pass.SetFloat("_ShoreWaveWindAngle", settings.Profile.WindAngle);
            });
        }

        renderGraph.SetResource(new WaterPrepassResult(oceanRenderResult, waterTriangleNormal, (Vector4)settings.Material.GetColor("_Color").linear, (Vector4)settings.Material.GetColor("_Extinction")));
    }
}