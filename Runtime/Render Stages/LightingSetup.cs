using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;
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
                var shadowIndex = 0;

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
                            var minValueRws = Vector3.positiveInfinity;
                            var maxValueRws = Vector3.negativeInfinity;
                            for (var z = 0; z < 2; z++)
                            {
                                for (var y = 0; y < 2; y++)
                                {
                                    for (var x = 0; x < 2; x++)
                                    {
                                        var eyeDepth = z == 0 ? cascadeStart : cascadeEnd;
                                        var clipDepth = (1.0f - eyeDepth / far) / (eyeDepth * (1.0f / near - 1.0f / far));

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

                                        var localPointRws = worldToLight.MultiplyPoint3x4((Vector3)worldPoint / worldPoint.w - camera.transform.position);

                                        minValueRws = Vector3.Min(minValueRws, localPointRws);
                                        maxValueRws = Vector3.Max(maxValueRws, localPointRws);
                                    }
                                }
                            }

                            // Calculate culling planes
                            // First get the planes from the view projection matrix
                            var viewProjectionMatrix = Matrix4x4Extensions.OrthoOffCenter(minValue.x, maxValue.x, minValue.y, maxValue.y, minValue.z, maxValue.z) * worldToLight;
                            var frustumPlanes = ArrayPool<Plane>.Get(6);
                            GeometryUtility.CalculateFrustumPlanes(viewProjectionMatrix, frustumPlanes);

                            var cullingPlanes = ListPool<Plane>.Get();
                            for (var k = 0; k < 6; k++)
                            {
                                // Skip near plane
                                if (k == 4)
                                    continue;

                                cullingPlanes.Add(frustumPlanes[k]);
                            }

                            var viewProjectionMatrixRws = Matrix4x4Extensions.OrthoOffCenter(minValueRws.x, maxValueRws.x, minValueRws.y, maxValueRws.y, minValueRws.z, maxValueRws.z) * worldToLight;

                            GeometryUtility.CalculateFrustumPlanes(viewProjectionMatrixRws, frustumPlanes);

                            var cullingPlanes1 = new CullingPlanes();
                            cullingPlanes1.Count = 5;
                            for (var k = 0; k < 6; k++)
                            {
                                // Skip near plane
                                if (k == 4)
                                    continue;

                                var index = k < 4 ? k : 4;
                                cullingPlanes1.SetCullingPlane(index, frustumPlanes[k]);
                            }

                            // Add any planes that face away from the light direction. This avoids rendering shadowcasters that can never cast a visible shadow
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

                            var width = maxValue.x - minValue.x;
                            var height = maxValue.y - minValue.y;
                            var depth = maxValue.z - minValue.z;

                            var gpuProjectionMatrix = new Matrix4x4
                            {
                                m00 = 2.0f / width,
                                m03 = (maxValue.x + minValue.x) / -width,
                                m11 = -2.0f / height,
                                m13 = -(maxValue.y + minValue.y) / -height,
                                m22 = 1.0f / (minValue.z - maxValue.z),
                                m23 = maxValue.z / depth,
                                m33 = 1.0f
                            };

                            var viewMatrixRWS = Matrix4x4Extensions.WorldToLocal(-camera.transform.position, lightRotation);
                            var vm = viewMatrixRWS;
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

                            directionalShadowMatrices.Add(shadowMatrix);

                            var directionalShadowRequest = new ShadowRequest(true, i, viewMatrixRWS, gpuProjectionMatrix, shadowSplitData, 0, cullingPlanes1);
                            directionalShadowRequests.Add(directionalShadowRequest);

                            directionalShadowTexelSizes.Add(new(width / settings.DirectionalShadowResolution, height / settings.DirectionalShadowResolution, 0.0f, maxValue.z));

                            //Debug.Log($"Width: {width}, Resolution: {settings.DirectionalShadowResolution}, Size: {settings.PcfFilterRadius / (width / settings.DirectionalShadowResolution)}");

                            cascadeCount++;
                        }

                        if (cascadeCount > 0)
                            shadowIndex = directionalShadowRequests.Count - cascadeCount;
                    }

                    Vector3 color = (Vector4)light.color.linear;

                    // Convert to rec2020
                    //var finalColor = new Vector4(
                    //    Vector3.Dot(color, new Vector3(0.627402f, 0.329292f, 0.043306f)),
                    //    Vector3.Dot(color, new Vector3(0.069095f, 0.919544f, 0.011360f)),
                    //    Vector3.Dot(color, new Vector3(0.016394f, 0.088028f, 0.895578f)),
                    //    1.0f
                    //);

                    var directionalLightData = new DirectionalLightData(color * light.intensity, shadowIndex, -light.transform.forward, cascadeCount, worldToLight);
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

                            // Todo: implemen
                            var cullingPlanes = new CullingPlanes();
                            cullingPlanes.Count = 0;

                            var shadowRequest = new ShadowRequest(isValid, i, viewMatrix, projectionMatrix, shadowSplitData, index, cullingPlanes);
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
                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
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

            var result = new Result(directionalShadowMatricesBuffer, directionalShadowTexelSizesBuffer, directionalLightBuffer, pointLightBuffer, directionalLightList.Count, pointLightList.Count);

            renderGraph.ResourceMap.SetRenderPassData(result, renderGraph.FrameIndex);
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
            private readonly int directionalLightCount;
            private readonly int pointLightCount;

            public Result(BufferHandle directionalMatrices, BufferHandle directionalShadowTexelSizes, BufferHandle directionalLights, BufferHandle pointLights, int directionalLightCount, int pointLightCount)
            {
                this.directionalMatrices = directionalMatrices ?? throw new ArgumentNullException(nameof(directionalMatrices));
                this.directionalShadowTexelSizes = directionalShadowTexelSizes ?? throw new ArgumentNullException(nameof(directionalShadowTexelSizes));
                this.directionalLights = directionalLights ?? throw new ArgumentNullException(nameof(directionalLights));
                this.pointLights = pointLights ?? throw new ArgumentNullException(nameof(pointLights));
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
            }
        }
    }
}