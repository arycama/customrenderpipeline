using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class LightingSetup
{
    private static readonly Plane[] frustumPlanes = new Plane[6];

    private static readonly IndexedString cascadeStrings = new IndexedString("Cascade ");
    private static readonly IndexedString faceStrings = new IndexedString("Face ");

    private readonly ShadowSettings settings;

    public LightingSetup(ShadowSettings settings)
    {
        this.settings = settings;

    }

    class PassData
    {
        public CullingResults cullingResults;
        public ShadowSettings settings;
        public ComputeBufferHandle directionalLightBuffer;
        public ComputeBufferHandle pointLightBuffer;
        public ComputeBufferHandle directionalMatrixBuffer;
        public ComputeBufferHandle directionalTexelSizeBuffer;
        public ComputeBufferHandle pointTexelSizeBuffer;
        public List<DirectionalLightData> directionalLightList;
        public List<ShadowRequest> directionalShadowRequests;
        public List<Matrix4x4> directionalShadowMatrices;
        public List<Vector4> directionalShadowTexelSizes;
        public List<PointLightData> pointLightList;
        public List<ShadowRequest> pointShadowRequests;
        public List<Vector4> pointShadowTexelSizes;
        public TextureHandle directionalShadows;
        public TextureHandle pointShadows;
    }

    public struct ResultData
    {
        public ComputeBufferHandle directionalLightBuffer;
        public ComputeBufferHandle pointLightBuffer;
        public ComputeBufferHandle directionalMatrixBuffer;
        public ComputeBufferHandle directionalTexelSizeBuffer;
        public ComputeBufferHandle pointTexelSizeBuffer;
        public TextureHandle directionalShadows;
        public TextureHandle pointShadows;
    }

    public ResultData Render(CullingResults cullingResults, Camera camera, RenderGraph renderGraph)
    {
        using var builder = renderGraph.AddRenderPass<PassData>("Shadow Setup", out var passData);

        passData.directionalLightList = ListPool<DirectionalLightData>.Get();
        passData.directionalShadowRequests = ListPool<ShadowRequest>.Get();
        passData.directionalShadowMatrices = ListPool<Matrix4x4>.Get();
        passData.directionalShadowTexelSizes = ListPool<Vector4>.Get();
        passData.pointLightList = ListPool<PointLightData>.Get();
        passData.pointShadowRequests = ListPool<ShadowRequest>.Get();
        passData.pointShadowTexelSizes = ListPool<Vector4>.Get();

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

                if (light.shadows != LightShadows.None && ((CullingResults)cullingResults).GetShadowCasterBounds(i, out var bounds))
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
                        passData.directionalShadowRequests.Add(directionalShadowRequest);

                        var shadowMatrix = (projectionMatrix * viewMatrix * lightToWorld).ConvertToAtlasMatrix();
                        passData.directionalShadowMatrices.Add(shadowMatrix);

                        var width = projectionMatrix.OrthoWidth();
                        var height = projectionMatrix.OrthoHeight();
                        var near = projectionMatrix.OrthoNear();
                        var far = projectionMatrix.OrthoFar();
                        passData.directionalShadowTexelSizes.Add(new(width, height, near, far));
                    }

                    if (cascadeCount > 0)
                        shadowIndex = passData.directionalShadowRequests.Count - cascadeCount;
                }

                var directionalLightData = new DirectionalLightData((Vector4)light.color.linear * light.intensity, shadowIndex, -light.transform.forward, cascadeCount, lightToWorld.inverse);
                passData.directionalLightList.Add(directionalLightData);
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
                        var isValid = false;
                        if (cullingResults.ComputePointShadowMatricesAndCullingPrimitives(i, (CubemapFace)j, 0.0f, out var viewMatrix, out var projectionMatrix, out var shadowSplitData))
                        {
                            visibleFaceMask |= 1 << j;
                            visibleFaceCount++;
                            isValid = true;
                        }

                        // To undo unity's builtin inverted culling for point shadows, flip the y axis.
                        // Y also needs to be done in the shader
                        viewMatrix.SetRow(1, -viewMatrix.GetRow(1));

                        var shadowRequest = new ShadowRequest(isValid, i, viewMatrix, projectionMatrix, shadowSplitData, j);
                        passData.pointShadowRequests.Add(shadowRequest);

                        near = projectionMatrix[2, 3] / (projectionMatrix[2, 2] - 1f);
                        far = projectionMatrix[2, 3] / (projectionMatrix[2, 2] + 1f);
                    }

                    if (visibleFaceCount > 0)
                        shadowIndex = (passData.pointShadowRequests.Count - visibleFaceCount) / 6;
                }

                var pointLightData = new PointLightData(light.transform.position, light.range, (Vector4)light.color.linear * light.intensity, shadowIndex, visibleFaceMask, near, far);
                passData.pointLightList.Add(pointLightData);
            }
        }

        camera.projectionMatrix = cameraProjectionMatrix;

        ResultData result;
        result.directionalLightBuffer = renderGraph.CreateComputeBuffer(new ComputeBufferDesc(Mathf.Max(1, passData.directionalLightList.Count), UnsafeUtility.SizeOf<DirectionalLightData>()));
        result.pointLightBuffer = renderGraph.CreateComputeBuffer(new ComputeBufferDesc(Mathf.Max(1, passData.pointLightList.Count), UnsafeUtility.SizeOf<PointLightData>()));
        result.directionalMatrixBuffer = renderGraph.CreateComputeBuffer(new ComputeBufferDesc(Mathf.Max(1, passData.directionalShadowMatrices.Count), UnsafeUtility.SizeOf<Matrix4x4>()));
        result.directionalTexelSizeBuffer = renderGraph.CreateComputeBuffer(new ComputeBufferDesc(Mathf.Max(1, passData.directionalShadowTexelSizes.Count), UnsafeUtility.SizeOf<Vector4>()));
        result.pointTexelSizeBuffer = renderGraph.CreateComputeBuffer(new ComputeBufferDesc(Mathf.Max(1, passData.pointShadowTexelSizes.Count), UnsafeUtility.SizeOf<Vector4>()));
        result.directionalShadows = renderGraph.CreateTexture(new TextureDesc(settings.DirectionalShadowResolution, settings.DirectionalShadowResolution)
        {
            clearBuffer = true,
            colorFormat = GraphicsFormat.D16_UNorm,
            depthBufferBits = DepthBits.Depth16,
            dimension = TextureDimension.Tex2DArray,
            slices = Mathf.Max(1, passData.directionalShadowRequests.Count)
        });

        result.pointShadows = renderGraph.CreateTexture(new TextureDesc(settings.PointShadowResolution, settings.PointShadowResolution)
        {
            clearBuffer = true,
            colorFormat = GraphicsFormat.D16_UNorm,
            depthBufferBits = DepthBits.Depth16,
            dimension = TextureDimension.CubeArray,
            slices = Mathf.Max(1, passData.pointShadowRequests.Count) * 6
        });

        passData.cullingResults = cullingResults;
        passData.settings = settings;
        passData.directionalLightBuffer = builder.WriteComputeBuffer(result.directionalLightBuffer);
        passData.pointLightBuffer = builder.WriteComputeBuffer(result.pointLightBuffer);
        passData.directionalMatrixBuffer = builder.WriteComputeBuffer(result.directionalMatrixBuffer);
        passData.directionalTexelSizeBuffer = builder.WriteComputeBuffer(result.directionalTexelSizeBuffer);
        passData.pointTexelSizeBuffer = builder.WriteComputeBuffer(result.pointTexelSizeBuffer);
        passData.directionalShadows = builder.WriteTexture(result.directionalShadows);
        passData.pointShadows = builder.WriteTexture(result.pointShadows);

        builder.SetRenderFunc<PassData>((data, context) =>
        {
            // Render Shadows
            context.cmd.BeginSample("Render Shadows");
            context.cmd.SetGlobalDepthBias(data.settings.ShadowBias, data.settings.ShadowSlopeBias);

            if (data.directionalShadowRequests.Count > 0)
            {
                // Process directional shadows
                context.cmd.SetGlobalFloat("_ZClip", 0);
                context.cmd.BeginSample("Directional Shadows");

                for (var i = 0; i < data.directionalShadowRequests.Count; i++)
                {
                    var shadowRequest = data.directionalShadowRequests[i];
                    context.cmd.SetRenderTarget(passData.directionalShadows, 0, CubemapFace.Unknown, i);

                    context.cmd.SetViewProjectionMatrices(shadowRequest.ViewMatrix, shadowRequest.ProjectionMatrix);
                    context.renderContext.ExecuteCommandBuffer(context.cmd);
                    context.cmd.Clear();

                    var shadowDrawingSettings = new ShadowDrawingSettings(data.cullingResults, shadowRequest.VisibleLightIndex) { splitData = shadowRequest.ShadowSplitData };
                    context.renderContext.DrawShadows(ref shadowDrawingSettings);
                }

                context.cmd.SetGlobalFloat("_ZClip", 1);
                context.cmd.EndSample("Directional Shadows");
            }

            // Process point shadows 
            // Setup shadow map for point shadows
            if (data.pointShadowRequests.Count > 0)
            {
                context.cmd.BeginSample("Point Shadows");

                for (var i = 0; i < data.pointShadowRequests.Count; i++)
                {
                    var shadowRequest = data.pointShadowRequests[i];
                    if (!shadowRequest.IsValid)
                        continue;

                    context.cmd.SetRenderTarget(passData.directionalShadows, 0, CubemapFace.Unknown, i);

                    context.cmd.SetViewProjectionMatrices(shadowRequest.ViewMatrix, shadowRequest.ProjectionMatrix);
                    context.renderContext.ExecuteCommandBuffer(context.cmd);
                    context.cmd.Clear();

                    var shadowDrawingSettings = new ShadowDrawingSettings(data.cullingResults, shadowRequest.VisibleLightIndex) { splitData = shadowRequest.ShadowSplitData };
                    context.renderContext.DrawShadows(ref shadowDrawingSettings);
                }

                context.cmd.EndSample("Point Shadows");
            }

            context.cmd.SetGlobalDepthBias(0f, 0f);

            // Set directional light data
            context.cmd.SetBufferData(data.directionalLightBuffer, data.directionalLightList);
            context.cmd.SetGlobalBuffer("_DirectionalLights", data.directionalLightBuffer);
            context.cmd.SetGlobalInt("_DirectionalLightCount", data.directionalLightList.Count);
            ListPool<DirectionalLightData>.Release(data.directionalLightList);

            context.cmd.SetGlobalTexture("_DirectionalShadows", data.directionalShadows);

            ListPool<ShadowRequest>.Release(data.directionalShadowRequests);

            // Update directional shadow matrices
            context.cmd.SetBufferData(data.directionalMatrixBuffer, data.directionalShadowMatrices);
            context.cmd.SetGlobalBuffer("_DirectionalMatrices", data.directionalMatrixBuffer);
            ListPool<Matrix4x4>.Release(data.directionalShadowMatrices);

            // Update directional shadow texel sizes
            context.cmd.SetBufferData(data.directionalTexelSizeBuffer, data.directionalShadowTexelSizes);
            context.cmd.SetGlobalBuffer("_DirectionalShadowTexelSizes", data.directionalTexelSizeBuffer);
            ListPool<Vector4>.Release(data.directionalShadowTexelSizes);

            // Set point light data
            context.cmd.SetBufferData(data.pointLightBuffer, data.pointLightList);
            context.cmd.SetGlobalBuffer("_PointLights", data.pointLightBuffer);
            context.cmd.SetGlobalInt("_PointLightCount", data.pointLightList.Count);
            ListPool<PointLightData>.Release(data.pointLightList);

            context.cmd.SetGlobalTexture("_PointShadows", data.pointShadows);

            ListPool<ShadowRequest>.Release(data.pointShadowRequests);

            context.cmd.SetBufferData(data.pointTexelSizeBuffer, data.pointShadowTexelSizes);
            context.cmd.SetGlobalBuffer("_PointShadowTexelSizes", data.pointTexelSizeBuffer);

            ListPool<Vector4>.Release(data.pointShadowTexelSizes);

            context.cmd.SetGlobalInt("_PcfSamples", data.settings.PcfSamples);
            context.cmd.SetGlobalFloat("_PcfRadius", data.settings.PcfRadius);
            context.cmd.SetGlobalInt("_BlockerSamples", data.settings.BlockerSamples);
            context.cmd.SetGlobalFloat("_BlockerRadius", data.settings.BlockerRadius);
            context.cmd.SetGlobalFloat("_PcssSoftness", data.settings.PcssSoftness);

            context.cmd.EndSample("Render Shadows"); 
        });

        return result;
    }
}
