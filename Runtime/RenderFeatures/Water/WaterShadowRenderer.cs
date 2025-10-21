using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class WaterShadowRenderer : WaterRendererBase
{
    public WaterShadowRenderer(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph, settings)
    {
    }

    public override void Render(Camera camera, ScriptableRenderContext context)
    {
        if (!settings.IsEnabled || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
            return;

        var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;
        var lightRotation = Quaternion.Identity;
        for (var i = 0; i < cullingResults.visibleLights.Length; i++)
        {
            var visibleLight = cullingResults.visibleLights[i];
            if (visibleLight.lightType != LightType.Directional)
                continue;

            lightRotation = visibleLight.localToWorldMatrix.rotation;
            break;
        }

		// TODO: Should be able to simply just define a box and not even worry about view position since we translate it anyway
		var viewPosition = camera.transform.position;
        var size = new Vector3(settings.ShadowRadius * 2, settings.Profile.MaxWaterHeight * 2, settings.ShadowRadius * 2);
        var min = new Vector3(-settings.ShadowRadius, -settings.Profile.MaxWaterHeight - viewPosition.y, -settings.ShadowRadius);

        var texelSize = settings.ShadowRadius * 2.0f / settings.ShadowResolution;

        var snappedViewPositionX = Math.Snap(viewPosition.x, texelSize) - viewPosition.x;
        var snappedViewPositionZ = Math.Snap(viewPosition.z, texelSize) - viewPosition.z;

        var worldToLight = Matrix4x4.Rotate(lightRotation.Inverse);
        Vector3 minValue = Vector3.positiveInfinity, maxValue = Vector3.negativeInfinity;

        for (var z = 0; z < 2; z++)
        {
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 2; x++)
                {
                    var worldPosition = min + Vector3.Scale(size, new Vector3(x, y, z));
                    worldPosition.x += snappedViewPositionX;
                    worldPosition.z += snappedViewPositionZ;

                    var localPosition = worldToLight.MultiplyPoint3x4(worldPosition);
                    minValue = Vector3.Min(minValue, localPosition);
                    maxValue = Vector3.Max(maxValue, localPosition);
                }
            }
        }

        // Calculate culling planes
        var width = maxValue.x - minValue.x;
        var height = maxValue.y - minValue.y;
        var depth = maxValue.z - minValue.z;
        var projectionMatrix = new Matrix4x4
        {
            m00 = 2.0f / width,
            m03 = (maxValue.x + minValue.x) / -width,
            m11 = -2.0f / height,
            m13 = -(maxValue.y + minValue.y) / -height,
            m22 = 1.0f / (minValue.z - maxValue.z),
            m23 = maxValue.z / depth,
            m33 = 1.0f
        };

        var viewProjectionMatrix = projectionMatrix * worldToLight;

        var frustumPlanes = ArrayPool<Plane>.Get(6);
        GeometryUtility.CalculateFrustumPlanes(viewProjectionMatrix, frustumPlanes);

        var cullingPlanes = new CullingPlanes() { Count = 6 };
        for (var j = 0; j < 6; j++)
        {
            cullingPlanes.SetCullingPlane(j, frustumPlanes[j]);
        }

        ArrayPool<Plane>.Release(frustumPlanes);

        var cullResult = Cull(viewPosition, cullingPlanes);

        var vm = worldToLight;
        var shadowMatrix = new Matrix4x4
        {
            m00 = vm.m00 / width,
            m01 = vm.m01 / width,
            m02 = vm.m02 / width,
            m03 = (vm.m03 - 0.5f * (maxValue.x + minValue.x)) / width + 0.5f,

            m10 = vm.m10 / height,
            m11 = vm.m11 / height,
            m12 = vm.m12 / height,
            m13 = (vm.m13 - 0.5f * (maxValue.y + minValue.y)) / height + 0.5f,

            m20 = -vm.m20 / depth,
            m21 = -vm.m21 / depth,
            m22 = -vm.m22 / depth,
            m23 = (-vm.m23 + 0.5f * (maxValue.z + minValue.z)) / depth + 0.5f,

            m33 = 1.0f
        };

        var waterShadow = renderGraph.GetTexture(settings.ShadowResolution, settings.ShadowResolution, GraphicsFormat.D16_UNorm, isExactSize: true, clearFlags: RTClearFlags.Depth);
        var waterIlluminance = renderGraph.GetTexture(settings.ShadowResolution, settings.ShadowResolution, GraphicsFormat.R16_UNorm, isExactSize: true);

        var passIndex = settings.Material.FindPass("WaterShadow");
        Assert.IsTrue(passIndex != -1, "Water Material does not contain a Water Shadow Pass");

        var profile = settings.Profile;
        var resolution = settings.Resolution;

        using (var pass = renderGraph.AddDrawProceduralIndirectIndexedRenderPass("Ocean Shadow", (viewProjectionMatrix, VerticesPerTileEdge, settings, viewPosition, cullingPlanes)))
        {
            pass.Initialize(settings.Material, indexBuffer, cullResult.IndirectArgsBuffer, MeshTopology.Quads, passIndex, depthBias: settings.ShadowBias, slopeDepthBias: settings.ShadowSlopeBias);
            pass.WriteDepth(waterShadow);
            pass.WriteTexture(waterIlluminance, RenderBufferLoadAction.DontCare);
            pass.ReadBuffer("_PatchData", cullResult.PatchDataBuffer);

            pass.AddRenderPassData<OceanFftResult>();
            pass.AddRenderPassData<WaterShoreMask.Result>(true);
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<FrameData>();
            pass.AddRenderPassData<LightingData>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetMatrix("_WaterShadowMatrix", data.viewProjectionMatrix);
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

        renderGraph.SetResource(new WaterShadowResult(waterShadow, shadowMatrix, 0.0f, (float)(maxValue.z - minValue.z), settings.Material.GetColor("_Extinction").Float3(), waterIlluminance));
    }
}