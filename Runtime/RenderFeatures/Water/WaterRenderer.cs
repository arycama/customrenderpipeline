using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class WaterRenderer : WaterRendererBase
{
	public WaterRenderer(RenderGraph renderGraph, WaterSettings settings, QuadtreeCull quadtreeCull) : base(renderGraph, settings, quadtreeCull)
	{
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
    {
        if (!settings.IsEnabled || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
            return;

        var passData = Cull(camera.transform.position, renderGraph.GetResource<CullingPlanesData>().cullingPlanes, camera.ViewSize(), true);

        // Writes (worldPos - displacementPos).xz. Uv coord is reconstructed later from delta and worldPosition (reconstructed from depth)
        var oceanRenderResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16G16_SFloat, isScreenTexture: true);

        // Also write triangleNormal to another texture with oct encoding. This allows reconstructing the derivative correctly to avoid mip issues on edges,
        // As well as backfacing triangle detection for rendering under the surface
        var waterTriangleNormal = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16G16_UNorm, isScreenTexture: true, clearFlags: RTClearFlags.Color);

        var passIndex = settings.Material.FindPass("Water");
        Assert.IsTrue(passIndex != -1, "Water Material has no Water Pass");

        var profile = settings.Profile;
        var resolution = settings.Resolution;
		var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().cullingPlanes;

		using (var pass = renderGraph.AddDrawProceduralIndirectIndexedRenderPass("Ocean Render", (VerticesPerTileEdge, renderGraph.FrameIndex, settings, camera, cullingPlanes)))
		{
			pass.Initialize(settings.Material, indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);

			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
			pass.WriteTexture(oceanRenderResult, RenderBufferLoadAction.DontCare);
			pass.WriteTexture(renderGraph.GetRTHandle<CameraVelocity>());
			pass.WriteTexture(waterTriangleNormal, RenderBufferLoadAction.DontCare);

			pass.ReadBuffer("PatchData", passData.PatchDataBuffer);

			pass.ReadResource<OceanFftResult>();
			pass.ReadResource<AtmospherePropertiesAndTables>();
			pass.ReadResource<TemporalAAData>();
			pass.ReadResource<WaterShoreMask.Result>(true);
			pass.ReadResource<ViewData>();
			pass.ReadResource<FrameData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetInt("_VerticesPerEdge", data.VerticesPerTileEdge);
				pass.SetInt("_VerticesPerEdgeMinusOne", data.VerticesPerTileEdge - 1);
				pass.SetFloat("_RcpVerticesPerEdgeMinusOne", 1f / (data.VerticesPerTileEdge - 1));
				pass.SetInt("_OceanTextureSlicePreviousOffset", ((data.FrameIndex & 1) == 0) ? 0 : 4);

				// Snap to quad-sized increments on largest cell
				var texelSize = data.settings.Size / (float)data.settings.PatchVertices;
				var positionX = Math.Snap(data.camera.transform.position.x, texelSize) - data.camera.transform.position.x - data.settings.Size * 0.5f;
				var positionZ = Math.Snap(data.camera.transform.position.z, texelSize) - data.camera.transform.position.z - data.settings.Size * 0.5f;
				pass.SetVector("_PatchScaleOffset", new Vector4(data.settings.Size / (float)data.settings.CellCount, data.settings.Size / (float)data.settings.CellCount, positionX, positionZ));

				pass.SetInt("_CullingPlanesCount", data.cullingPlanes.Count);
				var cullingPlanesArray = ArrayPool<Vector4>.Get(data.cullingPlanes.Count);
				for (var i = 0; i < data.cullingPlanes.Count; i++)
					cullingPlanesArray[i] = data.cullingPlanes.GetCullingPlaneVector4(i);

				pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
				ArrayPool<Vector4>.Release(cullingPlanesArray);

				pass.SetFloat("_ShoreWaveWindSpeed", data.settings.Profile.WindSpeed);
				pass.SetFloat("_ShoreWaveWindAngle", data.settings.Profile.WindAngle);
			});
		}

        renderGraph.SetResource(new WaterPrepassResult(oceanRenderResult, waterTriangleNormal, settings.Material.GetColor("_Color").LinearFloat3(), settings.Material.GetColor("_Extinction").Float3()));
    }
}