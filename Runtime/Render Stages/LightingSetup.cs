using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class LightingSetup : RenderFeature
    {
        private static readonly Plane[] frustumPlanes = new Plane[6];

        private readonly ShadowSettings settings;

        public LightingSetup(ShadowSettings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
        }

        public Result Render(CullingResults cullingResults, Camera camera)
        {
            var result = new Result();

            var directionalLightList = ListPool<DirectionalLightData>.Get();
            var directionalShadowRequests = ListPool<ShadowRequest>.Get();
            var directionalShadowMatrices = ListPool<Matrix4x4>.Get();
            var directionalShadowTexelSizes = ListPool<Vector4>.Get();
            var pointLightList = ListPool<PointLightData>.Get();
            var pointShadowRequests = ListPool<ShadowRequest>.Get();

            var cameraProjectionMatrix = camera.projectionMatrix;
            camera.ResetProjectionMatrix();

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
                    var lightToWorld = Matrix4x4.Rotate(lightRotation);

                    if (light.shadows != LightShadows.None && cullingResults.GetShadowCasterBounds(i, out var bounds))
                    {
                        Matrix4x4 viewMatrix, projectionMatrix;
                        ShadowSplitData shadowSplitData;
                        for (var j = 0; j < settings.ShadowCascades; j++)
                        {
                            if (settings.CloseFit)
                            {
                                viewMatrix = lightToWorld.inverse;

                                var cascadeStart = j == 0 ? camera.nearClipPlane : (settings.ShadowDistance - camera.nearClipPlane) * settings.ShadowCascadeSplits[j - 1];
                                var cascadeEnd = (j == settings.ShadowCascades - 1) ? settings.ShadowDistance : (settings.ShadowDistance - camera.nearClipPlane) * settings.ShadowCascadeSplits[j];

                                // Transform camera bounds to light space
                                var minValue = Vector3.positiveInfinity;
                                var maxValue = Vector3.negativeInfinity;
                                for (var z = 0; z < 2; z++)
                                {
                                    for (var y = 0; y < 2; y++)
                                    {
                                        for (var x = 0; x < 2; x++)
                                        {
                                            var worldPoint = camera.ViewportToWorldPoint(new(x, y, z == 0 ? cascadeStart : cascadeEnd));
                                            var localPoint = viewMatrix.MultiplyPoint3x4(worldPoint);
                                            minValue = Vector3.Min(minValue, localPoint);
                                            maxValue = Vector3.Max(maxValue, localPoint);
                                        }
                                    }
                                }

                                projectionMatrix = Matrix4x4.Ortho(minValue.x, maxValue.x, minValue.y, maxValue.y, minValue.z, maxValue.z);
                                viewMatrix.SetRow(2, -viewMatrix.GetRow(2));

                                // Calculate culling planes
                                var cullingPlanes = ListPool<Plane>.Get();

                                // First get the planes from the view projection matrix
                                var viewProjectionMatrix = projectionMatrix * viewMatrix;
                                GeometryUtility.CalculateFrustumPlanes(viewProjectionMatrix, frustumPlanes);
                                for (var k = 0; k < 6; k++)
                                {
                                    // Skip near plane
                                    if (k != 4)
                                        cullingPlanes.Add(frustumPlanes[k]);
                                }

                                // Now also add any main camera-frustum planes that are not facing away from the light
                                var lightDirection = -visibleLight.localToWorldMatrix.Forward();
                                GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
                                for (var k = 0; k < 6; k++)
                                {
                                    var plane = frustumPlanes[k];
                                    if (Vector3.Dot(plane.normal, lightDirection) > 0.0f)
                                        cullingPlanes.Add(plane);
                                }

                                shadowSplitData = new ShadowSplitData()
                                {
                                    cullingPlaneCount = cullingPlanes.Count,
                                    shadowCascadeBlendCullingFactor = 1
                                };

                                for (var k = 0; k < cullingPlanes.Count; k++)
                                {
                                    shadowSplitData.SetCullingPlane(k, cullingPlanes[k]);
                                }

                                ListPool<Plane>.Release(cullingPlanes);
                            }
                            else if (!cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(i, j, settings.ShadowCascades, settings.ShadowCascadeSplits, settings.DirectionalShadowResolution, light.shadowNearPlane, out viewMatrix, out projectionMatrix, out shadowSplitData))
                                continue;

                            cascadeCount++;
                            var directionalShadowRequest = new ShadowRequest(true, i, viewMatrix, projectionMatrix, shadowSplitData, 0);
                            directionalShadowRequests.Add(directionalShadowRequest);

                            var shadowMatrix = (projectionMatrix * viewMatrix * lightToWorld).ConvertToAtlasMatrix();
                            directionalShadowMatrices.Add(shadowMatrix);

                            var width = projectionMatrix.OrthoWidth();
                            var height = projectionMatrix.OrthoHeight();
                            var near = projectionMatrix.OrthoNear();
                            var far = projectionMatrix.OrthoFar();
                            directionalShadowTexelSizes.Add(new(width, height, near, far));
                        }

                        if (cascadeCount > 0)
                            shadowIndex = directionalShadowRequests.Count - cascadeCount;
                    }

                    var directionalLightData = new DirectionalLightData((Vector4)light.color.linear * light.intensity, shadowIndex, -light.transform.forward, cascadeCount, lightToWorld.inverse);
                    directionalLightList.Add(directionalLightData);
                }
                else if (visibleLight.lightType == LightType.Point)
                {
                    var near = light.shadowNearPlane;
                    var far = light.range;

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

                            // To undo unity's builtin inverted culling for point shadows, flip the y axis.
                            // Y also needs to be done in the shader
                            viewMatrix.SetRow(1, -viewMatrix.GetRow(1));

                            var shadowRequest = new ShadowRequest(isValid, i, viewMatrix, projectionMatrix, shadowSplitData, index);
                            pointShadowRequests.Add(shadowRequest);

                            near = projectionMatrix[2, 3] / (projectionMatrix[2, 2] - 1f);
                            far = projectionMatrix[2, 3] / (projectionMatrix[2, 2] + 1f);
                        }

                        if (visibleFaceCount > 0)
                            shadowIndex = (pointShadowRequests.Count - visibleFaceCount) / 6;
                    }

                    var pointLightData = new PointLightData(light.transform.position, light.range, (Vector4)light.color.linear * light.intensity, shadowIndex, visibleFaceMask, near, far);
                    pointLightList.Add(pointLightData);
                }
            }
            camera.projectionMatrix = cameraProjectionMatrix;

            // Directional lights
            var directionalLightBuffer = directionalLightList.Count == 0 ? renderGraph.EmptyBuffer : renderGraph.GetBuffer(directionalLightList.Count, UnsafeUtility.SizeOf<DirectionalLightData>());

            // Point lights
            var pointLightBuffer = pointLightList.Count == 0 ? renderGraph.EmptyBuffer : renderGraph.GetBuffer(pointLightList.Count, UnsafeUtility.SizeOf<PointLightData>());

            RTHandle directionalShadows;
            BufferHandle directionalMatrixBuffer, directionalTexelSizeBuffer;
            if (directionalShadowRequests.Count > 0)
            {
                directionalShadows = renderGraph.GetTexture(settings.DirectionalShadowResolution, settings.DirectionalShadowResolution, GraphicsFormat.D32_SFloat, false, directionalShadowRequests.Count, TextureDimension.Tex2DArray);
                directionalMatrixBuffer = renderGraph.GetBuffer(directionalShadowMatrices.Count, UnsafeUtility.SizeOf<Matrix4x4>());
                directionalTexelSizeBuffer = renderGraph.GetBuffer(directionalShadowTexelSizes.Count, UnsafeUtility.SizeOf<Vector4>());
            }
            else
            {
                directionalShadows = renderGraph.EmptyTextureArray;
                directionalMatrixBuffer = renderGraph.EmptyBuffer;
                directionalTexelSizeBuffer = renderGraph.EmptyBuffer;
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Render Directional Light Shadows"))
            {
                var data = pass.SetRenderFunction<Pass2Data>((command, context, pass, data) =>
                {
                    if (data.directionalShadowRequests.Count > 0)
                    {
                        // Render Shadows
                        command.SetGlobalDepthBias(data.shadowBias, data.shadowSlopeBias);
                        command.SetGlobalFloat("_ZClip", 0);

                        for (var i = 0; i < data.directionalShadowRequests.Count; i++)
                        {
                            var shadowRequest = data.directionalShadowRequests[i];
                            command.SetRenderTarget(data.directionalShadows, 0, CubemapFace.Unknown, i);
                            command.ClearRenderTarget(true, false, Color.clear);

                            command.SetViewProjectionMatrices(shadowRequest.ViewMatrix, shadowRequest.ProjectionMatrix);
                            command.BeginSample("Directional Shadows");

                            context.ExecuteCommandBuffer(command);
                            command.Clear();

                            var shadowDrawingSettings = new ShadowDrawingSettings(data.cullingResults, shadowRequest.VisibleLightIndex) { splitData = shadowRequest.ShadowSplitData };
                            context.DrawShadows(ref shadowDrawingSettings);

                            command.EndSample("Directional Shadows");
                            context.ExecuteCommandBuffer(command);
                            command.Clear();
                        }

                        command.SetGlobalDepthBias(0.0f, 0.0f);
                        command.SetGlobalFloat("_ZClip", 1);
                    }

                    ListPool<ShadowRequest>.Release(data.directionalShadowRequests);
                });

                data.shadowBias = settings.ShadowBias;
                data.shadowSlopeBias = settings.ShadowSlopeBias;
                data.directionalShadows = directionalShadows;
                data.directionalShadowRequests = directionalShadowRequests;
                data.cullingResults = cullingResults;

                result.directionalShadows = directionalShadows;
                result.directionalMatrices = directionalMatrixBuffer;
                result.directionalShadowTexelSizes = directionalTexelSizeBuffer;
            }

            // Process point shadows 
            var pointShadows = pointShadowRequests.Count == 0 ? renderGraph.EmptyCubemapArray : renderGraph.GetTexture(settings.PointShadowResolution, settings.PointShadowResolution, GraphicsFormat.D32_SFloat, false, pointShadowRequests.Count * 6, TextureDimension.CubeArray);

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Render Point Light Shadows"))
            {
                var data = pass.SetRenderFunction<Pass3Data>((command, context, pass, data) =>
                {
                    if (data.pointShadowRequests.Count > 0)
                    {
                        command.SetGlobalDepthBias(data.shadowBias, data.shadowSlopeBias);

                        for (var i = 0; i < data.pointShadowRequests.Count; i++)
                        {
                            var shadowRequest = data.pointShadowRequests[i];
                            if (!shadowRequest.IsValid)
                                continue;

                            command.SetRenderTarget(data.pointShadowsId, 0, CubemapFace.Unknown, i);
                            command.ClearRenderTarget(true, false, Color.clear);

                            command.SetViewProjectionMatrices(shadowRequest.ViewMatrix, shadowRequest.ProjectionMatrix);
                            command.BeginSample("Point Shadows");
                            context.ExecuteCommandBuffer(command);
                            command.Clear();

                            var shadowDrawingSettings = new ShadowDrawingSettings(data.cullingResults, shadowRequest.VisibleLightIndex) { splitData = shadowRequest.ShadowSplitData };
                            context.DrawShadows(ref shadowDrawingSettings);

                            command.EndSample("Point Shadows");
                            context.ExecuteCommandBuffer(command);
                            command.Clear();
                        }

                        command.SetGlobalDepthBias(0f, 0f);
                    }

                    ListPool<ShadowRequest>.Release(data.pointShadowRequests);
                });

                data.pointShadowRequests = pointShadowRequests;
                data.pointShadowsId = pointShadows;
                data.cullingResults = cullingResults;
                data.shadowBias = settings.PointShadowBias;
                data.shadowSlopeBias = settings.PointShadowSlopeBias;

                result.pointShadows = pointShadows;
                result.pcfSamples = settings.PcfSamples;
                result.pcfRadius = settings.PcfRadius;
                result.blockerSamples = settings.BlockerSamples;
                result.blockerRadius = settings.BlockerRadius;
                result.pcssSoftness = settings.PcssSoftness;
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Set Light Data"))
            {
                var data = pass.SetRenderFunction<Pass0Data>((command, context, pass, data) =>
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

                data.directionalMatrixBuffer = directionalMatrixBuffer;
                data.directionalShadowTexelSizes = directionalShadowTexelSizes;
                data.directionalShadowMatrices = directionalShadowMatrices;
                data.directionalTexelSizeBuffer = directionalTexelSizeBuffer;
                data.directionalLightBuffer = directionalLightBuffer;
                data.directionalLightList = directionalLightList;
                data.pointLightList = pointLightList;
                data.pointLightBuffer = pointLightBuffer;

                result.directionalLights = directionalLightBuffer;
                result.directionalLightCount = directionalLightList.Count;
                result.pointLights = pointLightBuffer;
                result.pointLightCount = pointLightList.Count;
            }

            return result;
        }

        private class Pass0Data
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

        private class Pass2Data
        {
            internal List<ShadowRequest> directionalShadowRequests;
            internal float shadowBias;
            internal float shadowSlopeBias;
            internal RTHandle directionalShadows;
            internal CullingResults cullingResults;
        }

        private class Pass3Data
        {
            internal List<ShadowRequest> pointShadowRequests;
            internal RTHandle pointShadowsId;
            internal CullingResults cullingResults;
            internal float shadowSlopeBias;
            internal float shadowBias;
        }

        public struct Result
        {
            public RTHandle directionalShadows;
            public BufferHandle directionalMatrices;
            public BufferHandle directionalShadowTexelSizes;
            public RTHandle pointShadows;
            public int pcfSamples;
            public float pcfRadius;
            public int blockerSamples;
            public float blockerRadius;
            public float pcssSoftness;
            public BufferHandle directionalLights;
            public int directionalLightCount;
            public BufferHandle pointLights;
            public int pointLightCount;
        }
    }
}