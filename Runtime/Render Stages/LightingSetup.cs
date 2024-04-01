using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class LightingSetup
    {
        private readonly ShadowSettings settings;
        private readonly RenderGraph renderGraph;

        public LightingSetup(ShadowSettings settings, RenderGraph renderGraph)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.renderGraph = renderGraph ?? throw new ArgumentNullException(nameof(renderGraph));
        }

        public void Render(CullingResults cullingResults, Matrix4x4 clipToWorld, float near, float far, Camera camera, out List<ShadowRequest> directionalShadowRequests, out List<ShadowRequest> pointShadowRequests)
        {
            var directionalLightList = ListPool<DirectionalLightData>.Get();
            directionalShadowRequests = ListPool<ShadowRequest>.Get();
            var directionalShadowMatrices = ListPool<Matrix4x4>.Get();
            var directionalShadowTexelSizes = ListPool<Vector4>.Get();
            var pointLightList = ListPool<PointLightData>.Get();
            pointShadowRequests = ListPool<ShadowRequest>.Get();

            // Setup lights/shadows
            for (var i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                var visibleLight = cullingResults.visibleLights[i];
                var light = visibleLight.light;
                var cascadeCount = 0;
                var shadowIndex = -1;

                if (visibleLight.lightType == LightType.Directional)
                {
                    var lightRotation = visibleLight.localToWorldMatrix.rotation;
                    var worldToLight = Matrix4x4.Rotate(Quaternion.Inverse(lightRotation));

                    if (light.shadows != LightShadows.None && cullingResults.GetShadowCasterBounds(i, out var bounds))
                    {
                        for (var j = 0; j < settings.ShadowCascades; j++)
                        {
                            var cascadeStart = j == 0 ? near : (settings.ShadowDistance - near) * settings.ShadowCascadeSplits[j - 1];
                            var cascadeEnd = (j == settings.ShadowCascades - 1) ? settings.ShadowDistance : (settings.ShadowDistance - near) * settings.ShadowCascadeSplits[j];

                            // Transform camera bounds to light space
                            var minValue = Vector3.positiveInfinity;
                            var maxValue = Vector3.negativeInfinity;
                            for (var z = 0; z < 2; z++)
                            {
                                for (var y = 0; y < 2; y++)
                                {
                                    for (var x = 0; x < 2; x++)
                                    {
                                        var depth = z == 0 ? cascadeStart : cascadeEnd;
                                        var clipDepth = (1.0f - depth / far) / (depth * (1.0f / near - 1.0f / far));

                                        var clipPoint = new Vector4
                                        (
                                            x * 2.0f - 1.0f,
                                            y * 2.0f - 1.0f,
                                            clipDepth,
                                            1.0f
                                        );

                                        var worldPoint = clipToWorld * clipPoint;
                                        var localPoint = worldToLight.MultiplyPoint3x4((Vector3)worldPoint / worldPoint.w);

                                        minValue = Vector3.Min(minValue, localPoint);
                                        maxValue = Vector3.Max(maxValue, localPoint);
                                    }
                                }
                            }

                            var localView = new Vector3(0.5f * (maxValue.x + minValue.x), 0.5f * (maxValue.y + minValue.y), minValue.z);
                            var viewMatrix = Matrix4x4Extensions.WorldToLocal(lightRotation * localView, lightRotation);

                            var projectionMatrix = new Matrix4x4
                            {
                                m00 = 2.0f / (maxValue.x - minValue.x),
                                m11 = 2.0f / (maxValue.y - minValue.y),
                                m22 = 2.0f / (maxValue.z - minValue.z),
                                m23 = -1.0f,
                                m33 = 1.0f
                            };

                            // Calculate culling planes
                            var cullingPlanes = ListPool<Plane>.Get();

                            // First get the planes from the view projection matrix
                            var viewProjectionMatrix = projectionMatrix * viewMatrix;
                            var frustumPlanes = ArrayPool<Plane>.Get(6);
                            GeometryUtility.CalculateFrustumPlanes(viewProjectionMatrix, frustumPlanes);
                            for (var k = 0; k < 6; k++)
                            {
                                // Skip near plane
                                if (k != 4)
                                    cullingPlanes.Add(frustumPlanes[k]);
                            }

                            var viewMatrixRWS = Matrix4x4Extensions.WorldToLocal(lightRotation * localView - camera.transform.position, lightRotation);

                            // Now also add any main camera-frustum planes that are not facing away from the light
                            var lightDirection = -visibleLight.localToWorldMatrix.Forward();
                            GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
                            for (var k = 0; k < 6; k++)
                            {
                                var plane = frustumPlanes[k];
                                if (Vector3.Dot(plane.normal, lightDirection) > 0.0f)
                                    cullingPlanes.Add(plane);
                            }
                            ArrayPool<Plane>.Release(frustumPlanes);

                            var shadowSplitData = new ShadowSplitData()
                            {
                                cullingPlaneCount = cullingPlanes.Count,
                                shadowCascadeBlendCullingFactor = 1
                            };

                            for (var k = 0; k < cullingPlanes.Count; k++)
                            {
                                shadowSplitData.SetCullingPlane(k, cullingPlanes[k]);
                            }

                            ListPool<Plane>.Release(cullingPlanes);

                            cascadeCount++;
                            var directionalShadowRequest = new ShadowRequest(true, i, viewMatrixRWS, projectionMatrix, shadowSplitData, 0);
                            directionalShadowRequests.Add(directionalShadowRequest);

                            var shadowMatrix = (projectionMatrix * viewMatrixRWS).ConvertToAtlasMatrix();
                            directionalShadowMatrices.Add(shadowMatrix);

                            var width = projectionMatrix.OrthoWidth();
                            var height = projectionMatrix.OrthoHeight();
                            directionalShadowTexelSizes.Add(new(width, height, projectionMatrix.OrthoNear(), projectionMatrix.OrthoFar()));
                        }

                        if (cascadeCount > 0)
                            shadowIndex = directionalShadowRequests.Count - cascadeCount;
                    }

                    var directionalLightData = new DirectionalLightData((Vector4)light.color.linear * light.intensity, shadowIndex, -light.transform.forward, cascadeCount, worldToLight);
                    directionalLightList.Add(directionalLightData);
                }
                else if (visibleLight.lightType == LightType.Point)
                {
                    var nearPlane = light.shadowNearPlane;
                    var farPlane = light.range;

                    var visibleFaceMask = 0;
                    var visibleFaceCount = 0;
                    if (light.shadows != LightShadows.None && cullingResults.GetShadowCasterBounds(i, out var bounds))
                    {
                        for (var j = 0; j < 6; j++)
                        {
                            // We also need to swap the top/bottom faces of the cubemap
                            var index = j;
                            if (j == 2) index = 3;
                            else if (j == 3) index = 2;

                            var isValid = false;
                            if (cullingResults.ComputePointShadowMatricesAndCullingPrimitives(i, (CubemapFace)index, 0.0f, out var viewMatrix, out var projectionMatrix, out var shadowSplitData))
                            {
                                visibleFaceMask |= 1 << index;
                                visibleFaceCount++;
                                isValid = true;
                            }

                            viewMatrix = Matrix4x4.TRS(light.transform.position - camera.transform.position, viewMatrix.inverse.rotation, Vector3.one).inverse;

                            // To undo unity's builtin inverted culling for point shadows, flip the y axis.
                            // Y also needs to be done in the shader
                            viewMatrix.SetRow(1, -viewMatrix.GetRow(1));

                            var shadowRequest = new ShadowRequest(isValid, i, viewMatrix, projectionMatrix, shadowSplitData, index);
                            pointShadowRequests.Add(shadowRequest);

                            nearPlane = projectionMatrix[2, 3] / (projectionMatrix[2, 2] - 1f);
                            farPlane = projectionMatrix[2, 3] / (projectionMatrix[2, 2] + 1f);
                        }

                        if (visibleFaceCount > 0)
                            shadowIndex = (pointShadowRequests.Count - visibleFaceCount) / 6;
                    }

                    var pointLightData = new PointLightData(light.transform.position - camera.transform.position, light.range, (Vector4)light.color.linear * light.intensity, shadowIndex, visibleFaceMask, nearPlane, farPlane);
                    pointLightList.Add(pointLightData);
                }
            }

            var directionalLightBuffer = directionalLightList.Count == 0 ? renderGraph.EmptyBuffer : renderGraph.GetBuffer(directionalLightList.Count, UnsafeUtility.SizeOf<DirectionalLightData>());
            var directionalShadowMatricesBuffer = directionalShadowRequests.Count == 0 ? renderGraph.EmptyBuffer : renderGraph.GetBuffer(directionalShadowMatrices.Count, UnsafeUtility.SizeOf<Matrix4x4>());
            var directionalShadowTexelSizesBuffer = directionalShadowRequests.Count == 0 ? renderGraph.EmptyBuffer : renderGraph.GetBuffer(directionalShadowTexelSizes.Count, UnsafeUtility.SizeOf<Vector4>());

            var pointLightBuffer = pointLightList.Count == 0 ? renderGraph.EmptyBuffer : renderGraph.GetBuffer(pointLightList.Count, UnsafeUtility.SizeOf<PointLightData>());

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Set Light Data"))
            {
                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    command.SetBufferData(data.directionalMatrixBuffer, data.directionalShadowMatrices);
                    ListPool<Matrix4x4>.Release(data.directionalShadowMatrices);

                    command.SetBufferData(data.directionalTexelSizeBuffer, data.directionalShadowTexelSizes);
                    ListPool<Vector4>.Release(data.directionalShadowTexelSizes);

                    command.SetBufferData(data.directionalLightBuffer, data.directionalLightList);
                    ListPool<DirectionalLightData>.Release(data.directionalLightList);

                    command.SetBufferData(data.pointLightBuffer, data.pointLightList);
                    ListPool<PointLightData>.Release(data.pointLightList);
                });

                data.directionalMatrixBuffer = directionalShadowMatricesBuffer;
                data.directionalShadowTexelSizes = directionalShadowTexelSizes;
                data.directionalShadowMatrices = directionalShadowMatrices;
                data.directionalTexelSizeBuffer = directionalShadowTexelSizesBuffer;
                data.directionalLightBuffer = directionalLightBuffer;
                data.directionalLightList = directionalLightList;
                data.pointLightList = pointLightList;
                data.pointLightBuffer = pointLightBuffer;
            }

            var result = new Result(directionalShadowMatricesBuffer, directionalShadowTexelSizesBuffer, directionalLightBuffer, pointLightBuffer, settings.PcfSamples, settings.PcfRadius, settings.BlockerSamples, settings.BlockerRadius, settings.PcssSoftness, directionalLightList.Count, pointLightList.Count);

            renderGraph.ResourceMap.SetRenderPassData(result);
        }

        private class PassData
        {
            internal List<DirectionalLightData> directionalLightList;
            internal BufferHandle directionalLightBuffer;
            internal List<PointLightData> pointLightList;
            internal BufferHandle pointLightBuffer;
            internal BufferHandle directionalMatrixBuffer;
            internal List<Vector4> directionalShadowTexelSizes;
            internal List<Matrix4x4> directionalShadowMatrices;
            internal BufferHandle directionalTexelSizeBuffer;
        }

        public readonly struct Result : IRenderPassData
        {
            private readonly BufferHandle directionalMatrices;
            private readonly BufferHandle directionalShadowTexelSizes;
            private readonly BufferHandle directionalLights;
            private readonly BufferHandle pointLights;
            private readonly int pcfSamples;
            private readonly float pcfRadius;
            private readonly int blockerSamples;
            private readonly float blockerRadius;
            private readonly float pcssSoftness;
            private readonly int directionalLightCount;
            private readonly int pointLightCount;

            public Result(BufferHandle directionalMatrices, BufferHandle directionalShadowTexelSizes, BufferHandle directionalLights, BufferHandle pointLights, int pcfSamples, float pcfRadius, int blockerSamples, float blockerRadius, float pcssSoftness, int directionalLightCount, int pointLightCount)
            {
                this.directionalMatrices = directionalMatrices ?? throw new ArgumentNullException(nameof(directionalMatrices));
                this.directionalShadowTexelSizes = directionalShadowTexelSizes ?? throw new ArgumentNullException(nameof(directionalShadowTexelSizes));
                this.directionalLights = directionalLights ?? throw new ArgumentNullException(nameof(directionalLights));
                this.pointLights = pointLights ?? throw new ArgumentNullException(nameof(pointLights));
                this.pcfSamples = pcfSamples;
                this.pcfRadius = pcfRadius;
                this.blockerSamples = blockerSamples;
                this.blockerRadius = blockerRadius;
                this.pcssSoftness = pcssSoftness;
                this.directionalLightCount = directionalLightCount;
                this.pointLightCount = pointLightCount;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadBuffer("_DirectionalMatrices", directionalMatrices);
                pass.ReadBuffer("_DirectionalLights", directionalLights);
                pass.ReadBuffer("_PointLights", pointLights);
                pass.ReadBuffer("_DirectionalShadowTexelSizes", directionalShadowTexelSizes);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetInt(command, "_DirectionalLightCount", directionalLightCount);
                pass.SetInt(command, "_PointLightCount", pointLightCount);

                pass.SetInt(command, "_PcfSamples", pcfSamples);
                pass.SetFloat(command, "_PcfRadius", pcfRadius);
                pass.SetInt(command, "_BlockerSamples", blockerSamples);
                pass.SetFloat(command, "_BlockerRadius", blockerRadius);
                pass.SetFloat(command, "_PcssSoftness", pcssSoftness);
            }
        }
    }
}