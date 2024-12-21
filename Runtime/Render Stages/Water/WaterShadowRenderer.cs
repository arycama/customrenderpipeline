using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public class WaterShadowRenderer : WaterRendererBase
    {
        public WaterShadowRenderer(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph, settings)
        {
        }

        public override void Render()
        {
            if (!settings.IsEnabled)
                return;

            var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;
            var lightRotation = Quaternion.identity;
            for (var i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                var visibleLight = cullingResults.visibleLights[i];
                if (visibleLight.lightType != LightType.Directional)
                    continue;

                lightRotation = visibleLight.localToWorldMatrix.rotation;
                break;
            }

            var viewData = renderGraph.GetResource<ViewData>();

            // TODO: Should be able to simply just define a box and not even worry about view position since we translate it anyway
            var size = new Vector3(settings.ShadowRadius * 2, settings.Profile.MaxWaterHeight * 2, settings.ShadowRadius * 2);
            var min = new Vector3(-settings.ShadowRadius, -settings.Profile.MaxWaterHeight - viewData.ViewPosition.y, -settings.ShadowRadius);

            var texelSize = settings.ShadowRadius * 2.0f / settings.ShadowResolution;

            var snappedViewPositionX = MathUtils.Snap(viewData.ViewPosition.x, texelSize) - viewData.ViewPosition.x;
            var snappedViewPositionZ = MathUtils.Snap(viewData.ViewPosition.z, texelSize) - viewData.ViewPosition.z;

            var worldToLight = Matrix4x4.Rotate(Quaternion.Inverse(lightRotation));
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

            var cullResult = Cull(viewData.ViewPosition, cullingPlanes);

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

            var waterShadow = renderGraph.GetTexture((int)settings.ShadowResolution, (int)settings.ShadowResolution, GraphicsFormat.D16_UNorm);
            var waterIlluminance = renderGraph.GetTexture((int)settings.ShadowResolution, (int)settings.ShadowResolution, GraphicsFormat.R16_UNorm);

            var passIndex = settings.Material.FindPass("WaterShadow");
            Assert.IsTrue(passIndex != -1, "Water Material does not contain a Water Shadow Pass");

            var profile = settings.Profile;
            var resolution = settings.Resolution;

            using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Ocean Shadow"))
            {
                pass.Initialize(settings.Material, indexBuffer, cullResult.IndirectArgsBuffer, MeshTopology.Quads, passIndex, depthBias: settings.ShadowBias, slopeDepthBias: settings.ShadowSlopeBias);
                pass.WriteDepth(waterShadow);
                pass.WriteTexture(waterIlluminance, RenderBufferLoadAction.DontCare);
                pass.ConfigureClear(RTClearFlags.Depth);
                pass.ReadBuffer("_PatchData", cullResult.PatchDataBuffer);

                pass.AddRenderPassData<OceanFftResult>();
                pass.AddRenderPassData<WaterShoreMask.Result>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<DirectionalLightInfo>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetMatrix("_WaterShadowMatrix", viewProjectionMatrix);
                    pass.SetInt("_VerticesPerEdge", VerticesPerTileEdge);
                    pass.SetInt("_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
                    pass.SetFloat("_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));

                    // Snap to quad-sized increments on largest cell
                    var texelSize = settings.Size / (float)settings.PatchVertices;
                    var positionX = MathUtils.Snap(viewData.ViewPosition.x, texelSize) - viewData.ViewPosition.x - settings.Size * 0.5f;
                    var positionZ = MathUtils.Snap(viewData.ViewPosition.z, texelSize) - viewData.ViewPosition.z - settings.Size * 0.5f;
                    pass.SetVector("_PatchScaleOffset", new Vector4(settings.Size / (float)settings.CellCount, settings.Size / (float)settings.CellCount, positionX, positionZ));

                    var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
                    for (var i = 0; i < cullingPlanes.Count; i++)
                        cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

                    pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                    ArrayPool<Vector4>.Release(cullingPlanesArray);

                    pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
                    pass.SetFloat("_ShoreWaveWindSpeed", settings.Profile.WindSpeed);
                    pass.SetFloat("_ShoreWaveWindAngle", settings.Profile.WindAngle);
                });
            }

            renderGraph.SetResource(new WaterShadowResult(waterShadow, shadowMatrix, 0.0f, (float)(maxValue.z - minValue.z), settings.Material.GetVector("_Extinction"), waterIlluminance));
        }
    }
}