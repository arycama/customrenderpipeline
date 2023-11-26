using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class LightingSetup
{
    private static readonly Plane[] frustumPlanes = new Plane[6];

    private static readonly int directionalShadowsId = Shader.PropertyToID("_DirectionalShadows");
    private static readonly int pointShadowsId = Shader.PropertyToID("_PointShadows");
    private static readonly IndexedString cascadeStrings = new IndexedString("Cascade ");
    private static readonly IndexedString faceStrings = new IndexedString("Face ");

    private readonly ShadowSettings settings;

    public LightingSetup(ShadowSettings settings)
    {
        this.settings = settings;

    }

    class PassData
    {
        public Camera camera;
        public CullingResults cullingResults;
        public ShadowSettings settings;
        public ComputeBufferHandle directionalLightBuffer;
        public ComputeBufferHandle pointLightBuffer;
        public ComputeBufferHandle directionalMatrixBuffer;
        public ComputeBufferHandle directionalTexelSizeBuffer;
        public ComputeBufferHandle pointTexelSizeBuffer;
    }

    public void Render(CullingResults cullingResults, Camera camera, RenderGraph renderGraph)
    {
        var directionalLightList = ListPool<DirectionalLightData>.Get();
        var directionalShadowRequests = ListPool<ShadowRequest>.Get();
        var directionalShadowMatrices = ListPool<Matrix4x4>.Get();
        var directionalShadowTexelSizes = ListPool<Vector4>.Get();
        var pointLightList = ListPool<PointLightData>.Get();
        var pointShadowRequests = ListPool<ShadowRequest>.Get();
        var pointShadowTexelSizes = ListPool<Vector4>.Get();

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

        var directionalLightBuffer = renderGraph.CreateComputeBuffer(new ComputeBufferDesc(Mathf.Max(1, directionalLightList.Count), UnsafeUtility.SizeOf<DirectionalLightData>()));
        var pointLightBuffer = renderGraph.CreateComputeBuffer(new ComputeBufferDesc(Mathf.Max(1, pointLightList.Count), UnsafeUtility.SizeOf<PointLightData>()));
        var directionalMatrixBuffer = renderGraph.CreateComputeBuffer(new ComputeBufferDesc(Mathf.Max(1, directionalShadowMatrices.Count), UnsafeUtility.SizeOf<Matrix4x4>()));
        var directionalTexelSizeBuffer = renderGraph.CreateComputeBuffer(new ComputeBufferDesc(Mathf.Max(1, directionalShadowTexelSizes.Count), UnsafeUtility.SizeOf<Vector4>()));
        var pointTexelSizeBuffer = renderGraph.CreateComputeBuffer(new ComputeBufferDesc(Mathf.Max(1, pointShadowTexelSizes.Count), UnsafeUtility.SizeOf<Vector4>()));

        using var builder = renderGraph.AddRenderPass<PassData>("Shadow Setup", out var passData);
        passData.camera = camera;
        passData.cullingResults = cullingResults;
        passData.settings = settings;
        passData.directionalLightBuffer = builder.WriteComputeBuffer(directionalLightBuffer);
        passData.pointLightBuffer = builder.WriteComputeBuffer(pointLightBuffer);
        passData.directionalMatrixBuffer = builder.WriteComputeBuffer(directionalMatrixBuffer);
        passData.directionalTexelSizeBuffer = builder.WriteComputeBuffer(directionalTexelSizeBuffer);
        passData.pointTexelSizeBuffer = builder.WriteComputeBuffer(pointTexelSizeBuffer);

        builder.SetRenderFunc<PassData>((data, context) =>
        {
            // Render Shadows
            context.cmd.SetGlobalDepthBias(passData.settings.ShadowBias, passData.settings.ShadowSlopeBias);

            if (directionalShadowRequests.Count > 0)
            {
                // Process directional shadows
                context.cmd.SetGlobalFloat("_ZClip", 0);

                // Setup shadow map for directional shadows
                var directionalShadowsDescriptor = new RenderTextureDescriptor(passData.settings.DirectionalShadowResolution, passData.settings.DirectionalShadowResolution, RenderTextureFormat.Shadowmap, 16)
                {
                    dimension = TextureDimension.Tex2DArray,
                    volumeDepth = directionalShadowRequests.Count,
                };

                context.cmd.GetTemporaryRT(directionalShadowsId, directionalShadowsDescriptor);
                context.cmd.SetRenderTarget(directionalShadowsId, 0, CubemapFace.Unknown, -1);
                context.cmd.ClearRenderTarget(true, false, Color.clear);

                for (var i = 0; i < directionalShadowRequests.Count; i++)
                {
                    var shadowRequest = directionalShadowRequests[i];
                    context.cmd.SetRenderTarget(directionalShadowsId, 0, CubemapFace.Unknown, i);

                    context.cmd.SetViewProjectionMatrices(shadowRequest.ViewMatrix, shadowRequest.ProjectionMatrix);
                    context.renderContext.ExecuteCommandBuffer(context.cmd);
                    context.cmd.Clear();

                    var shadowDrawingSettings = new ShadowDrawingSettings(data.cullingResults, shadowRequest.VisibleLightIndex) { splitData = shadowRequest.ShadowSplitData };
                    context.renderContext.DrawShadows(ref shadowDrawingSettings);
                }

                context.cmd.SetGlobalFloat("_ZClip", 1);
            }

            // Process point shadows 
            // Setup shadow map for point shadows
            if (pointShadowRequests.Count > 0)
            {
                var pointShadowsDescriptor = new RenderTextureDescriptor(passData.settings.PointShadowResolution, passData.settings.PointShadowResolution, RenderTextureFormat.Shadowmap, 16)
                {
                    dimension = TextureDimension.CubeArray,
                    volumeDepth = pointShadowRequests.Count * 6,
                };

                context.cmd.GetTemporaryRT(pointShadowsId, pointShadowsDescriptor);
                context.cmd.SetRenderTarget(pointShadowsId, 0, CubemapFace.Unknown, -1);
                context.cmd.ClearRenderTarget(true, false, Color.clear);

                for (var i = 0; i < pointShadowRequests.Count; i++)
                {
                    var shadowRequest = pointShadowRequests[i];
                    if (!shadowRequest.IsValid)
                        continue;

                    context.cmd.SetRenderTarget(pointShadowsId, 0, CubemapFace.Unknown, i);

                    context.cmd.SetViewProjectionMatrices(shadowRequest.ViewMatrix, shadowRequest.ProjectionMatrix);
                    context.renderContext.ExecuteCommandBuffer(context.cmd);
                    context.cmd.Clear();

                    var shadowDrawingSettings = new ShadowDrawingSettings(data.cullingResults, shadowRequest.VisibleLightIndex) { splitData = shadowRequest.ShadowSplitData };
                    context.renderContext.DrawShadows(ref shadowDrawingSettings);
                }
            }

            context.cmd.SetGlobalDepthBias(0f, 0f);

            // Set directional light data
            context.cmd.SetBufferData(passData.directionalLightBuffer, directionalLightList);
            context.cmd.SetGlobalBuffer("_DirectionalLights", passData.directionalLightBuffer);
            context.cmd.SetGlobalInt("_DirectionalLightCount", directionalLightList.Count);
            ListPool<DirectionalLightData>.Release(directionalLightList);

            if (directionalShadowRequests.Count > 0)
            {
                context.cmd.SetGlobalTexture(directionalShadowsId, directionalShadowsId);
            }
            else
            {
                var emptyShadowId = Shader.PropertyToID("_EmptyShadow");
                context.cmd.GetTemporaryRT(emptyShadowId, new RenderTextureDescriptor(1, 1, RenderTextureFormat.Shadowmap) { dimension = TextureDimension.Tex2DArray });
                context.cmd.SetGlobalTexture(directionalShadowsId, emptyShadowId);
            }

            ListPool<ShadowRequest>.Release(directionalShadowRequests);

            // Update directional shadow matrices
            context.cmd.SetBufferData(passData.directionalMatrixBuffer, directionalShadowMatrices);
            context.cmd.SetGlobalBuffer("_DirectionalMatrices", passData.directionalMatrixBuffer);
            ListPool<Matrix4x4>.Release(directionalShadowMatrices);

            // Update directional shadow texel sizes
            context.cmd.SetBufferData(passData.directionalTexelSizeBuffer, directionalShadowTexelSizes);
            context.cmd.SetGlobalBuffer("_DirectionalShadowTexelSizes", passData.directionalTexelSizeBuffer);
            ListPool<Vector4>.Release(directionalShadowTexelSizes);

            // Set point light data
            context.cmd.SetBufferData(passData.pointLightBuffer, pointLightList);
            context.cmd.SetGlobalBuffer("_PointLights", passData.pointLightBuffer);
            context.cmd.SetGlobalInt("_PointLightCount", pointLightList.Count);
            ListPool<PointLightData>.Release(pointLightList);

            if (pointShadowRequests.Count > 0)
            {
                context.cmd.SetGlobalTexture(pointShadowsId, pointShadowsId);
            }
            else
            {
                var emptyShadowCubemapId = Shader.PropertyToID("_EmptyShadowCubemap");
                context.cmd.GetTemporaryRT(emptyShadowCubemapId, new RenderTextureDescriptor(1, 1, RenderTextureFormat.Shadowmap) { dimension = TextureDimension.CubeArray, volumeDepth = 6 });
                context.cmd.SetGlobalTexture(pointShadowsId, emptyShadowCubemapId);
            }

            ListPool<ShadowRequest>.Release(pointShadowRequests);

            context.cmd.SetBufferData(passData.pointTexelSizeBuffer, pointShadowTexelSizes);
            context.cmd.SetGlobalBuffer("_PointShadowTexelSizes", passData.pointTexelSizeBuffer);

            ListPool<Vector4>.Release(pointShadowTexelSizes);

            context.cmd.SetGlobalInt("_PcfSamples", passData.settings.PcfSamples);
            context.cmd.SetGlobalFloat("_PcfRadius", passData.settings.PcfRadius);
            context.cmd.SetGlobalInt("_BlockerSamples", passData.settings.BlockerSamples);
            context.cmd.SetGlobalFloat("_BlockerRadius", passData.settings.BlockerRadius);
            context.cmd.SetGlobalFloat("_PcssSoftness", passData.settings.PcssSoftness);
        });
    }
}
