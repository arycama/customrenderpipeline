using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;
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

        public void Render(CullingResults cullingResults, Matrix4x4 clipToWorld, float near, float far, Camera camera)
        {
            var result = new Result();

            var directionalLightList = ListPool<DirectionalLightData>.Get();
            var directionalShadowRequests = ListPool<ShadowRequest>.Get();
            var directionalShadowMatrices = ListPool<Matrix4x4>.Get();
            var directionalShadowTexelSizes = ListPool<Vector4>.Get();
            var pointLightList = ListPool<PointLightData>.Get();
            var pointShadowRequests = ListPool<ShadowRequest>.Get();

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

            // Directional lights
            var directionalLightBuffer = directionalLightList.Count == 0 ? renderGraph.EmptyBuffer : renderGraph.GetBuffer(directionalLightList.Count, UnsafeUtility.SizeOf<DirectionalLightData>());

            // Point lights
            var pointLightBuffer = pointLightList.Count == 0 ? renderGraph.EmptyBuffer : renderGraph.GetBuffer(pointLightList.Count, UnsafeUtility.SizeOf<PointLightData>());

            if (directionalShadowRequests.Count > 0)
            {
                result.directionalShadows = renderGraph.GetTexture(settings.DirectionalShadowResolution, settings.DirectionalShadowResolution, GraphicsFormat.D32_SFloat, directionalShadowRequests.Count, TextureDimension.Tex2DArray);
                result.directionalMatrices = renderGraph.GetBuffer(directionalShadowMatrices.Count, UnsafeUtility.SizeOf<Matrix4x4>());
                result.directionalShadowTexelSizes = renderGraph.GetBuffer(directionalShadowTexelSizes.Count, UnsafeUtility.SizeOf<Vector4>());

                // Render Shadows
                for (var i = 0; i < directionalShadowRequests.Count; i++)
                {
                    using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Render Directional Light Shadows"))
                    {
                        // Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
                        pass.WriteTexture(result.directionalShadows);

                        var data = pass.SetRenderFunction<Pass2Data>((command, context, pass, data) =>
                        {
                            command.SetGlobalDepthBias(data.shadowBias, data.shadowSlopeBias);
                            command.SetGlobalFloat("_ZClip", 0);

                            command.SetRenderTarget(data.directionalShadows, 0, CubemapFace.Unknown, data.cascade);
                            command.ClearRenderTarget(true, false, Color.clear);

                            command.SetGlobalMatrix("_WorldToView", data.shadowRequest.ViewMatrix);
                            command.SetGlobalMatrix("_WorldToClip", GL.GetGPUProjectionMatrix(data.shadowRequest.ProjectionMatrix, true) * data.shadowRequest.ViewMatrix);
                            command.SetGlobalVector("_ViewPosition", data.viewPosition);

                            command.BeginSample("Directional Shadows");
                            context.ExecuteCommandBuffer(command);
                            command.Clear();

                            var shadowDrawingSettings = new ShadowDrawingSettings(data.cullingResults, data.shadowRequest.VisibleLightIndex) { splitData = data.shadowRequest.ShadowSplitData };
                            context.DrawShadows(ref shadowDrawingSettings);

                            command.EndSample("Directional Shadows");
                            command.SetGlobalDepthBias(0.0f, 0.0f);
                            command.SetGlobalFloat("_ZClip", 1);
                        });

                        data.shadowBias = settings.ShadowBias;
                        data.shadowSlopeBias = settings.ShadowSlopeBias;
                        data.directionalShadows = result.directionalShadows;
                        data.cullingResults = cullingResults;
                        data.cascade = i;
                        data.shadowRequest = directionalShadowRequests[i];
                        data.viewPosition = camera.transform.position;
                    }
                }
            }
            else
            {
                result.directionalShadows = renderGraph.EmptyTextureArray;
                result.directionalMatrices = renderGraph.EmptyBuffer;
                result.directionalShadowTexelSizes = renderGraph.EmptyBuffer;
            }

            ListPool<ShadowRequest>.Release(directionalShadowRequests);

            // Process point shadows 
            if (pointShadowRequests.Count == 0)
            {
                result.pointShadows = renderGraph.EmptyCubemapArray;
            }
            else
            {
                result.pointShadows = renderGraph.GetTexture(settings.PointShadowResolution, settings.PointShadowResolution, GraphicsFormat.D32_SFloat, pointShadowRequests.Count, TextureDimension.CubeArray);

                for (var i = 0; i < pointShadowRequests.Count; i++)
                {
                    var shadowRequest = pointShadowRequests[i];
                    if (!shadowRequest.IsValid)
                        continue;

                    using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Render Point Light Shadows"))
                    {
                        // Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
                        pass.WriteTexture(result.pointShadows);

                        var data = pass.SetRenderFunction<Pass3Data>((command, context, pass, data) =>
                        {
                            command.SetGlobalDepthBias(data.shadowBias, data.shadowSlopeBias);

                            command.SetRenderTarget(data.pointShadows, 0, CubemapFace.Unknown, data.faceIndex);
                            command.ClearRenderTarget(true, false, Color.clear);

                            pass.SetMatrix(command, "_WorldToView", data.shadowRequest.ViewMatrix);
                            pass.SetMatrix(command, "_WorldToClip", GL.GetGPUProjectionMatrix(data.shadowRequest.ProjectionMatrix, true) * data.shadowRequest.ViewMatrix);
                            command.SetGlobalVector("_ViewPosition", data.viewPosition);

                            command.BeginSample("Point Shadows");
                            context.ExecuteCommandBuffer(command);
                            command.Clear();

                            var shadowDrawingSettings = new ShadowDrawingSettings(data.cullingResults, data.shadowRequest.VisibleLightIndex) { splitData = data.shadowRequest.ShadowSplitData };
                            context.DrawShadows(ref shadowDrawingSettings);

                            command.EndSample("Point Shadows");
                            command.SetGlobalDepthBias(0f, 0f);
                        });

                        data.pointShadows = result.pointShadows;
                        data.cullingResults = cullingResults;
                        data.shadowBias = settings.PointShadowBias;
                        data.shadowSlopeBias = settings.PointShadowSlopeBias;
                        data.shadowRequest = shadowRequest;
                        data.faceIndex = i;
                        data.viewPosition = camera.transform.position;
                    }
                }
            }

            ListPool<ShadowRequest>.Release(pointShadowRequests);

            result.pcfSamples = settings.PcfSamples;
            result.pcfRadius = settings.PcfRadius;
            result.blockerSamples = settings.BlockerSamples;
            result.blockerRadius = settings.BlockerRadius;
            result.pcssSoftness = settings.PcssSoftness;

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

                data.directionalMatrixBuffer = result.directionalMatrices;
                data.directionalShadowTexelSizes = directionalShadowTexelSizes;
                data.directionalShadowMatrices = directionalShadowMatrices;
                data.directionalTexelSizeBuffer = result.directionalShadowTexelSizes;
                data.directionalLightBuffer = directionalLightBuffer;
                data.directionalLightList = directionalLightList;
                data.pointLightList = pointLightList;
                data.pointLightBuffer = pointLightBuffer;

                result.directionalLights = directionalLightBuffer;
                result.directionalLightCount = directionalLightList.Count;
                result.pointLights = pointLightBuffer;
                result.pointLightCount = pointLightList.Count;
            }

            renderGraph.ResourceMap.SetRenderPassData(result);
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
            internal float shadowBias;
            internal float shadowSlopeBias;
            internal RTHandle directionalShadows;
            internal CullingResults cullingResults;
            internal int cascade;
            internal ShadowRequest shadowRequest;
            internal Vector3 viewPosition;
        }

        private class Pass3Data
        {
            internal RTHandle pointShadows;
            internal CullingResults cullingResults;
            internal float shadowSlopeBias;
            internal float shadowBias;
            internal ShadowRequest shadowRequest;
            internal int faceIndex;
            internal Vector3 viewPosition;
        }

        public struct Result : IRenderPassData
        {
            public RTHandle directionalShadows;
            public RTHandle pointShadows;
            public BufferHandle directionalMatrices;
            public BufferHandle directionalShadowTexelSizes;
            public BufferHandle directionalLights;
            public BufferHandle pointLights;
            public int pcfSamples;
            public float pcfRadius;
            public int blockerSamples;
            public float blockerRadius;
            public float pcssSoftness;
            public int directionalLightCount;
            public int pointLightCount;

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_DirectionalShadows", directionalShadows);
                pass.ReadTexture("_PointShadows", pointShadows);
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