using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;

public class WaterShadowRenderer : WaterRendererBase
{
	public WaterShadowRenderer(RenderGraph renderGraph, WaterSettings settings, QuadtreeCull quadtreeCull) : base(renderGraph, settings, quadtreeCull)
	{
	}

	public override void Render(ViewRenderData viewRenderData)
    {
        if (!settings.IsEnabled || (viewRenderData.camera.cameraType != CameraType.Game && viewRenderData.camera.cameraType != CameraType.SceneView))
            return;

        if (!renderGraph.TryGetResource<LightingData>(out var lightingData))
            return;

		var viewPosition = viewRenderData.transform.position;
        var size = new Float3(settings.ShadowRadius * 2, settings.Profile.MaxWaterHeight * 2, settings.ShadowRadius * 2);
        var min = new Float3(-settings.ShadowRadius, -settings.Profile.MaxWaterHeight - viewPosition.y, -settings.ShadowRadius);

        var texelSize = settings.ShadowRadius * 2.0f / settings.ShadowResolution;

        var snappedViewPositionX = Math.Snap(viewPosition.x, texelSize) - viewPosition.x;
        var snappedViewPositionZ = Math.Snap(viewPosition.z, texelSize) - viewPosition.z;
        var worldToView = Float4x4.Rotate(lightingData.light0Rotation.Inverse);

		Bounds bounds = default;
        for (int z = 0, i = 0; z < 2; z++)
        {
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 2; x++, i++)
                {
                    var worldPosition = size * new Float3(x, y, z) + min;
                    worldPosition.x += snappedViewPositionX;
                    worldPosition.z += snappedViewPositionZ;

                    var localPoint = worldToView.MultiplyPoint3x4(worldPosition);
                    bounds = i == 0 ? new Bounds(localPoint, Float3.Zero) : bounds.Encapsulate(localPoint);
                }
            }
        }

        // Calculate culling planes
        var viewToClip = Float4x4.OrthoReverseZ(bounds);
        var worldToClip = viewToClip.Mul(worldToView);

        var cullingPlanes = new CullingPlanes() { Count = 6 };
        for (var j = FrustumPlane.Left; j < FrustumPlane.Count; j++)
            cullingPlanes.SetCullingPlane((int)j, worldToClip.GetFrustumPlane(j));

        var cullResult = Cull(viewPosition, cullingPlanes, viewRenderData.viewSize, false);
        var shadowMatrix = Float4x4.OrthoReverseZSample(bounds).Mul(worldToView);

        var waterShadow = renderGraph.GetTexture(settings.ShadowResolution, GraphicsFormat.D16_UNorm, isExactSize: true, clear: true);
        var waterIlluminance = renderGraph.GetTexture(settings.ShadowResolution, GraphicsFormat.R8_UNorm, isExactSize: true, clear: true);

        var passIndex = settings.Material.FindPass("WaterShadow");
        Assert.IsTrue(passIndex != -1, "Water Material does not contain a Water Shadow Pass");

        using (var pass = renderGraph.AddDrawProceduralIndirectIndexedRenderPass("Ocean Shadow", (worldToClip, VerticesPerTileEdge, settings, viewPosition, cullingPlanes)))
        {
            pass.Initialize(settings.Material, indexBuffer, cullResult.IndirectArgsBuffer, settings.ShadowResolution, 1, MeshTopology.Quads, passIndex, depthBias: settings.ShadowBias, slopeDepthBias: settings.ShadowSlopeBias);
            pass.WriteDepth(waterShadow);
            pass.WriteTexture(waterIlluminance);
            pass.ReadBuffer("PatchData", cullResult.PatchDataBuffer);

            pass.ReadResource<OceanFftResult>();
            pass.ReadResource<ViewData>();
            pass.ReadResource<FrameData>();
            pass.ReadResource<LightingData>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetMatrix("_WaterShadowMatrix", data.worldToClip);
                pass.SetInt("_VerticesPerEdge", data.VerticesPerTileEdge);
                pass.SetInt("_VerticesPerEdgeMinusOne", data.VerticesPerTileEdge - 1);
                pass.SetFloat("_RcpVerticesPerEdgeMinusOne", 1f / (data.VerticesPerTileEdge - 1));

                // Snap to quad-sized increments on largest cell
                var texelSize = data.settings.Size / (float)data.settings.PatchVertices;
                var positionX = Math.Snap(data.viewPosition.x, texelSize) - data.viewPosition.x - data.settings.Size * 0.5f;
                var positionZ = Math.Snap(data.viewPosition.z, texelSize) - data.viewPosition.z - data.settings.Size * 0.5f;
                pass.SetVector("_PatchScaleOffset", new Vector4(data.settings.Size / (float)data.settings.CellCount, data.settings.Size / (float)data.settings.CellCount, positionX, positionZ));

                var cullingPlanesArray = ArrayPool<Vector4>.Get(data.cullingPlanes.Count);
                for (var i = 0; i < data.cullingPlanes.Count; i++)
                    cullingPlanesArray[i] = data.cullingPlanes.GetCullingPlaneVector4(i);

                pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                ArrayPool<Vector4>.Release(cullingPlanesArray);

                pass.SetInt("_CullingPlanesCount", data.cullingPlanes.Count);
                pass.SetFloat("_ShoreWaveWindSpeed", data.settings.Profile.WindSpeed);
                pass.SetFloat("_ShoreWaveWindAngle", data.settings.Profile.WindAngle);
            });
        }

        var transmittance = settings.Material.GetColor("Transmittance").LinearFloat3();
        var transmittanceDistance = settings.Material.GetFloat("TransmittanceDistance");
        var extinction = -new Float3(Math.Log(transmittance.x), Math.Log(transmittance.y), Math.Log(transmittance.z)) / transmittanceDistance;

        renderGraph.SetResource(new WaterShadowResult(waterShadow, shadowMatrix, 0.0f, (float)(bounds.Max.z - bounds.Min.z), extinction, waterIlluminance));
    }
}