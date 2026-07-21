using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using Unmath;
using static Unmath.Math;
using Quaternion = Unmath.Quaternion;

namespace CustomRenderPipeline
{
    public partial class LightingSetup : ViewRenderFeature
    {
        private readonly LightingSettings settings;
        private readonly NativeList<LightShadowCasterCullingInfo> perLightInfos = new(1, Allocator.Persistent);
        private readonly NativeList<ShadowSplitData> splitBuffer = new(1, Allocator.Persistent);
        private readonly LightCulling.Settings lightCullingSettings;

        private LightData[] pointLights = new LightData[8];
        private float[] pointLightDepths = new float[8];
        private int[] lightDepthBins, lightDepthMinMax;

        public LightingSetup(RenderGraph renderGraph, LightingSettings settings, LightCulling.Settings lightCullingSettings) : base(renderGraph)
        {
            this.settings = settings;
        }

        protected override void Cleanup(bool disposing)
        {
            perLightInfos.Dispose();
            splitBuffer.Dispose();
        }

        public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
        {
            var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;
            var directionalShadowRequests = ListPool<ShadowRequest>.Get();
            var pointShadowRequests = ListPool<ShadowRequest>.Get();
            var spotShadowRequests = ListPool<ShadowRequest>.Get();
            var directionalShadowMatrices = ListPool<Float3x4>.Get();
            var directionalCascadeSizes = ListPool<Float4>.Get();

            // Find first 2 directional lights
            Float3 lightColor0 = Float3.Zero, lightColor1 = Float3.Zero;
            Quaternion lightRotation0 = Quaternion.Identity, lightRotation1 = Quaternion.Identity;
            var dirLightCount = 0;

            var lightCount = cullingResults.visibleLights.Length;

            Array.Resize(ref pointLights, Max(pointLights.Length, lightCount));
            Array.Resize(ref pointLightDepths, Max(pointLightDepths.Length, lightCount));
            var pointLightCount = 0;

            var n = viewPassData.near;
            var f = settings.DirectionalShadowDistance;
            var m = (float)settings.DirectionalCascadeCount;
            var c = Pow(Max(1e-3f, settings.CascadeUniformity), 2.2f);

            perLightInfos.Clear();
            splitBuffer.Clear();

            for (var i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                var visibleLight = cullingResults.visibleLights[i];
                var lightToWorld = (Float4x4)visibleLight.localToWorldMatrix;
				var lightColor = ColorspaceUtility.Rec709ToRec2020(visibleLight.finalColor.Float3());
				var lightForward = lightToWorld.Forward;
                var lightPosition = lightToWorld.Translation;
                var lightRotation = lightToWorld.Rotation;
                var splitRange = new RangeInt(0, 0);

                if (visibleLight.lightType == LightType.Directional)
                {
                    dirLightCount++;
                    if (dirLightCount == 1)
                    {
                        lightRotation0 = lightRotation;
                        lightColor0 = lightColor;

#if UNITY_EDITOR
                        // The default scene light only has an intensity of 1, set it to sun
                        if (viewPassData.cameraType == CameraType.SceneView && !UnityEditor.SceneView.currentDrawingSceneView.sceneLighting || viewPassData.cameraType == CameraType.Preview)
                            lightColor0 *= 120000;
#endif
                    }
                    else if (dirLightCount < 3)
                    {
                        lightRotation1 = lightRotation;
                        lightColor1 = lightColor;
                    }
                }

                // TODO: May need adjusting for spot lights?
                var light = visibleLight.light;
                var shadowIndex = uint.MaxValue;
                ushort splitExclusionMask = 0;

                var size = light.areaSize;
                if (light.shadows != LightShadows.None)
                {
                    var hasShadowBounds = cullingResults.GetShadowCasterBounds(i, out var shadowCasterBounds);

                    if (light.type == LightType.Directional)
                    {
                        // ref https://developer.nvidia.com/gpugems/gpugems3/part-ii-light-and-shadows/chapter-10-parallel-split-shadow-maps-programmable-gpus
                        var worldToView = Float4x4.Rotate(lightRotation.Inverse);
                        var cameraToWorld = Float4x4.TRS(viewPassData.position, viewPassData.rotation, 1);
                        var cameraToView = worldToView.Mul(cameraToWorld);

                        float GetFrustumDepth(int j)
                        {
                            var L = Rcp(c);
                            var M = Log2(c * (f - n) + 1);
                            var N = n - Rcp(c);
                            var x = j / m;
                            return L * Exp2(M * x) + N;
                        }

                        splitRange = new RangeInt(splitBuffer.Length, settings.DirectionalCascadeCount);
                        for (var j = 0; j < settings.DirectionalCascadeCount; j++)
                        {
                            // Transform camera split bounds to light space
                            var near = GetFrustumDepth(j);
                            var far = GetFrustumDepth(j + 1);

                            // TODO: Can this be done in camera relative space to simplify, since we need the final viewProj matrix in camera relative space anyway? Though culling planes need to be in world space
                            var viewBounds = Geometry.GetFrustumBounds(viewPassData.tanHalfFov, near, far, cameraToView);
                            var viewToClip = Float4x4.OrthoReverseZ(-viewBounds.extents.x, viewBounds.extents.x, -viewBounds.extents.y, viewBounds.extents.y, 0, viewBounds.Size.z);

                            var worldViewPosition = viewBounds.center;
                            worldViewPosition.z = viewBounds.Min.z;
                            worldViewPosition = lightToWorld.MultiplyPoint3x4(worldViewPosition);

                            var worldToCascade = Float4x4.WorldToLocal(worldViewPosition, lightRotation);

                            var shadowSplitData = CalculateShadowSplitData(viewToClip.Mul(worldToCascade), lightRotation.Forward, true);

                            var cameraInverseTranslation = Float4x4.Translate(viewPassData.position);
                            worldToCascade = worldToCascade.Mul(cameraInverseTranslation);

                            directionalShadowRequests.Add(new(i, worldToCascade, viewToClip, shadowSplitData, -1, Float3.Zero, hasShadowBounds, 0, viewBounds.Size.z, worldViewPosition, lightRotation, viewBounds.Size.x, viewBounds.Size.y, settings.DirectionalShadowResolution));
                            directionalShadowMatrices.Add((Float3x4)Float4x4.OrthoReverseZSample(-viewBounds.extents.x, viewBounds.extents.x, -viewBounds.extents.y, viewBounds.extents.y, 0, viewBounds.Size.z).Mul(worldToCascade));

                            // Note it could be max(cascadeTexelSize * 0.5, but this means we'd get no anti-aliasing on the min filter size)
                            var worldUnitsPerTexel = viewBounds.Size.xy / settings.DirectionalShadowResolution;
                            var filterSize = Float2.Max(worldUnitsPerTexel, settings.DirectionalBlockerDistance * Radians(settings.SunAngularDiameter) * 0.5f);
                            var filterRadius = Float2.Min(settings.DirectionalMaxFilterSize, Float2.Ceil(filterSize * settings.DirectionalShadowResolution * 0.5f));
                            var rcpFilterSize = worldUnitsPerTexel / filterSize;
                            directionalCascadeSizes.Add(new Float4(rcpFilterSize, filterRadius));

                            splitBuffer.Add(shadowSplitData);
                        }
                    }

                    if (visibleLight.lightType == LightType.Point)
                    {
                        shadowIndex = (uint)pointShadowRequests.Count;
                        splitRange = new RangeInt(splitBuffer.Length, 6);

                        for (var j = 0; j < 6; j++)
                        {
                            var forward = Float4x4.lookAtList[j];
                            var rotation = Quaternion.LookRotation(forward, Float4x4.upVectorList[j]);
                            var worldToView = Float4x4.WorldToLocal(lightPosition, rotation);
                            var viewToClip = Float4x4.PerspectiveReverseZ(1, light.shadowNearPlane, light.range);
                            var worldToClip = viewToClip.Mul(worldToView);
                            var shadowSplitData = CalculateShadowSplitData(worldToClip, forward, false);

                            // Convert to camera relative
                            var cameraInverseTranslation = Float4x4.Translate(viewPassData.position);
                            worldToView = worldToView.Mul(cameraInverseTranslation);

                            pointShadowRequests.Add(new(i, worldToView, viewToClip, shadowSplitData, j, lightPosition, hasShadowBounds, light.shadowNearPlane, light.range, lightPosition, lightRotation, 1, 1, settings.PointShadowResolution));
                            splitBuffer.Add(shadowSplitData);
                        }
                    }

                    // TODO: Box/Pyramid/Area/Disc
                    if (visibleLight.lightType == LightType.Spot)
                    {
                        var worldToView = Float4x4.WorldToLocal(lightPosition, lightRotation);
                        var viewToClip = Float4x4.PerspectiveReverseZ(Tan(0.5f * Radians(light.spotAngle)) * new Float2(1.0f, size.x / size.y), light.shadowNearPlane, light.range);
                        var worldToClip = viewToClip.Mul(worldToView);
                        var shadowSplitData = CalculateShadowSplitData(worldToClip, light.transform.forward, false);

                        // Convert to camera relative
                        var cameraInverseTranslation = Float4x4.Translate(viewPassData.position);
                        worldToView = worldToView.Mul(cameraInverseTranslation);

                        shadowIndex = (uint)spotShadowRequests.Count;
                        var shadowRequest = new ShadowRequest(i, worldToView, viewToClip, shadowSplitData, -1, lightPosition, hasShadowBounds, light.shadowNearPlane, light.range, lightPosition, lightRotation, light.spotAngle, size.x / size.y, settings.SpotShadowResolution);
                        spotShadowRequests.Add(shadowRequest);

                        splitRange = new RangeInt(splitBuffer.Length, 1);
                        splitBuffer.Add(shadowSplitData);
                    }
                }

                var projectionType = light.type == LightType.Directional || light.type == LightType.Box ? BatchCullingProjectionType.Orthographic : BatchCullingProjectionType.Perspective;
                var lightShadowCasterCullingInfo = new LightShadowCasterCullingInfo
                {
                    projectionType = projectionType,
                    splitExclusionMask = splitExclusionMask,
                    splitRange = splitRange
                };

                perLightInfos.Add(lightShadowCasterCullingInfo);

                if (visibleLight.lightType == LightType.Spot || visibleLight.lightType == LightType.Point)
                {
                    var position = lightToWorld.Translation;
                    var distanceScale = Flip(Sq(Rcp(visibleLight.range)), !visibleLight.light.enableSpotReflector);
                    var halfAngle = 0.5f * Radians(visibleLight.spotAngle);
                    var innerConePercent = light.innerSpotAngle / visibleLight.spotAngle;
                    var outerCosHalfAngle = Cos(halfAngle);
                    var innerCosHalfAngle = Cos(halfAngle * innerConePercent);
                    var isSpot = visibleLight.lightType == LightType.Spot;
                    var angleScale = isSpot ? Rcp(outerCosHalfAngle - innerCosHalfAngle) : 0.0f;
                    var angleOffset = isSpot ? outerCosHalfAngle * angleScale : 1.0f;

                    // Calcualte center of spot light cone
                    var cullingSphere = new Float4(position, visibleLight.range);
                    if (visibleLight.lightType == LightType.Spot)
                    {
                        if (outerCosHalfAngle < Sqrt(0.5f))
                        {
                            cullingSphere.xyz += outerCosHalfAngle * visibleLight.range * lightForward;
                            cullingSphere.w *= Sin(halfAngle);
                        }
                        else
                        {
                            cullingSphere.xyz += visibleLight.range / (2.0f * outerCosHalfAngle) * lightForward;
                            cullingSphere.w /= 2.0f * outerCosHalfAngle;
                        }
                    }

                    // Convert to view space
                    cullingSphere.xyz = viewPassData.rotation.InverseRotate(cullingSphere.xyz - viewPassData.position);

                    var lightData = new LightData
                    (
                        lightToWorld.Translation - viewPassData.position,
                        Rcp(Sq(light.range)),
                        lightToWorld.Forward,
                        angleScale,
                        lightColor,
                        angleOffset,
                        cullingSphere,
                        lightToWorld.Right,
                        (uint)light.type,
                        lightToWorld.Up,
                        shadowIndex,
                        size,
                        1.0f + light.range / (light.shadowNearPlane - light.range),
                        light.shadowNearPlane * light.range / (light.range - light.shadowNearPlane)
                    );

                    pointLights[pointLightCount] = lightData;
                    pointLightDepths[pointLightCount] = cullingSphere.z;
                    pointLightCount++;
                }
            }

            var infos = new ShadowCastersCullingInfos()
            {
                perLightInfos = perLightInfos.AsArray(),
                splitBuffer = splitBuffer.AsArray()
            };

            context.CullShadowCasters(cullingResults, infos);

            // Set final matrices
            var directionalShadowMatricesBuffer = renderGraph.GetBuffer(Max(1, directionalShadowMatrices.Count), UnsafeUtility.SizeOf<Float3x4>());
            using (var pass = renderGraph.AddGenericRenderPass("Set Directional Shadow Matrices", (directionalShadowMatrices, directionalShadowMatricesBuffer)))
            {
                pass.WriteBuffer("", directionalShadowMatricesBuffer);
                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    command.SetBufferData(pass.GetBuffer(data.directionalShadowMatricesBuffer), data.directionalShadowMatrices);
                    ListPool<Float3x4>.Release(data.directionalShadowMatrices);
                });
            }

            var directionalCascadeSizesBuffer = renderGraph.GetBuffer(Max(1, directionalCascadeSizes.Count), UnsafeUtility.SizeOf<Float4>());
            using (var pass = renderGraph.AddGenericRenderPass("Set Directional Cascade Sizes", (directionalCascadeSizes, directionalCascadeSizesBuffer)))
            {
                pass.WriteBuffer("", directionalCascadeSizesBuffer);
                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    command.SetBufferData(pass.GetBuffer(data.directionalCascadeSizesBuffer), data.directionalCascadeSizes);
                    ListPool<Float4>.Release(data.directionalCascadeSizes);
                });
            }

            // Decoding params
            var F = m * Rcp(Log2(c * (f - n) + 1));
            var E = Log2(c) * F;
            var G = Rcp(c) - n;

            var fadeScale = -Rcp(settings.DirectionalFadeLength);
            var fadeOffset = settings.DirectionalShadowDistance * Rcp(settings.DirectionalFadeLength);

            var lightingData = renderGraph.SetConstantBuffer(new LightingDataStruct
            (
                -lightRotation0.Forward,
                directionalShadowRequests.Count,
                lightColor0,
                dirLightCount,
                -lightRotation1.Forward,
                settings.DirectionalMaxFilterSize,
                lightColor1,
                settings.DirectionalBlockerDistance,
                new Float4(E, F, G, 0),
                fadeScale,
                fadeOffset,
                settings.DirectionalShadowResolution,
                Rcp(settings.DirectionalShadowResolution)
            ));

            renderGraph.SetResource(new LightingData(lightRotation0, lightColor0, lightRotation1, lightColor1, lightingData, directionalShadowMatricesBuffer, directionalCascadeSizesBuffer));

            // Sort lights by view depth
            Array.Sort(pointLightDepths, pointLights);

            // Resize Z bins if needed and clear
            Array.Resize(ref lightDepthBins, lightCullingSettings.DepthSlices);
            Array.Clear(lightDepthBins, 0, lightDepthBins.Length);

            Array.Resize(ref lightDepthMinMax, lightCullingSettings.DepthSlices);
            for (var i = 0; i < lightDepthMinMax.Length; i++)
                lightDepthMinMax[i] = BitPack(ushort.MaxValue, 16, 0) | BitPack(0, 16, 16);

            var numSlices = lightCullingSettings.DepthSlices;
            var linearToLogScale = numSlices / Log2(viewPassData.far / viewPassData.near);
            var linearToLogOffset = -Log2(viewPassData.near) * linearToLogScale;

            // Add sorted lights to list
            var binWidth = viewPassData.far / lightCullingSettings.DepthSlices;
            for (var i = 0; i < pointLightCount; i++)
            {
                var light = pointLights[i];

                // Calculate view min and max depth
                var minZ = light.cullingSphere.z - light.cullingSphere.w;
                var maxZ = light.cullingSphere.z + light.cullingSphere.w;

                // BitOr with covered Z bins
                //var minBin = Max(0, (int)(Log2(minZ) * linearToLogScale + linearToLogOffset));
                //var maxBin = Min(lightCullingSettings.DepthSlices - 1, (int)(Log2(maxZ) * linearToLogScale + linearToLogOffset));

                var minBin = Max(0, (int)(minZ / binWidth));
                var maxBin = Min(lightCullingSettings.DepthSlices - 1, (int)(maxZ / binWidth));

                for (var j = minBin; j <= maxBin; j++)
                {
                    lightDepthBins[j] |= i;

                    var currentMinMax = lightDepthMinMax[j];

                    var currentMin = BitUnpack(currentMinMax, 16, 0);
                    var currentMax = BitUnpack(currentMinMax, 16, 16);

                    currentMin = Min(currentMin, i);
                    currentMax = Max(currentMax, i);

                    lightDepthMinMax[j] = BitPack(currentMin, 16, 0) | BitPack(currentMax, 16, 16);
                }
            }

            var pointLightBuffer = pointLightCount == 0 ? renderGraph.EmptyBuffer : renderGraph.GetBuffer(pointLightCount, UnsafeUtility.SizeOf<LightData>());
            var lightDepthBinBuffer = renderGraph.GetBuffer(lightCullingSettings.DepthSlices);
            var lightDepthMinMaxBuffer = renderGraph.GetBuffer(lightCullingSettings.DepthSlices);

            using (var pass = renderGraph.AddGenericRenderPass("Set Light Data", (pointLights, pointLightCount, pointLightBuffer, lightDepthBinBuffer, lightDepthBins, lightDepthMinMaxBuffer, lightDepthMinMax)))
            {
                pass.WriteBuffer("", pointLightBuffer);
                pass.WriteBuffer("", lightDepthBinBuffer);
                pass.WriteBuffer("", lightDepthMinMaxBuffer);
                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    command.SetBufferData(pass.GetBuffer(data.pointLightBuffer), data.pointLights, 0, 0, data.pointLightCount);
                    command.SetBufferData(pass.GetBuffer(data.lightDepthBinBuffer), data.lightDepthBins);
                    command.SetBufferData(pass.GetBuffer(data.lightDepthMinMaxBuffer), data.lightDepthMinMax);
                });
            }

            var tileCountX = DivRoundUp(viewPassData.viewSize.x, lightCullingSettings.TileSize);
            var tileCountY = DivRoundUp(viewPassData.viewSize.y, lightCullingSettings.TileSize);
            var lightIndexCount = DivRoundUp(pointLightCount, 32);

            var pointLightData = renderGraph.SetConstantBuffer
            ((
                (float)lightCullingSettings.TileSize,
                pointLightCount,
                DivRoundUp(viewPassData.viewSize.x, lightCullingSettings.TileSize),
                lightIndexCount,
                lightCullingSettings.DepthSlices,
                binWidth,
                linearToLogScale,
                linearToLogOffset
            ));

            renderGraph.SetResource(new PointLightData(pointLightData, pointLightBuffer, pointLightCount, lightDepthBinBuffer, lightDepthMinMaxBuffer));
            renderGraph.SetResource(new ShadowRequestsData(directionalShadowRequests, pointShadowRequests, spotShadowRequests));
        }

        private ShadowSplitData CalculateShadowSplitData(Float4x4 viewProjectionMatrix, bool skipNearPlane)
        {
            var shadowSplitData = new ShadowSplitData() { shadowCascadeBlendCullingFactor = 1 };
            for (var i = FrustumPlane.Left; i < FrustumPlane.Count; i++)
            {
                if (!skipNearPlane || i != FrustumPlane.Near)
                    shadowSplitData.SetCullingPlane(shadowSplitData.cullingPlaneCount++, viewProjectionMatrix.GetFrustumPlane(i));
            }

            return shadowSplitData;
        }

        /// <summary> Add any planes that face away from the light direction. This avoids rendering shadowcasters that can never cast a visible shadow </summary>
        private ShadowSplitData CalculateShadowSplitData(Float4x4 viewProjectionMatrix, Float3 forward, bool skipNearPlane)
        {
            var shadowSplitData = CalculateShadowSplitData(viewProjectionMatrix, skipNearPlane);
            for (var i = FrustumPlane.Left; i < FrustumPlane.Count; i++)
            {
                var plane = viewProjectionMatrix.GetFrustumPlane(i);
                if (plane.normal.Dot(forward) < 0.0f)
                    shadowSplitData.SetCullingPlane(shadowSplitData.cullingPlaneCount++, plane);
            }

            return shadowSplitData;
        }
    }

    internal struct LightingDataStruct
    {
        public Float3 lightDirection0;
        public int Count;
        public Float3 lightColor0;
        public int dirLightCount;
        public Float3 lightDirection1;
        public float Item6;
        public Float3 lightColor1;
        public float DirectionalBlockerDistance;
        public Float4 Item9;
        public float fadeScale;
        public float fadeOffset;
        public float Item12;
        public float Item13;

        public LightingDataStruct(Float3 lightDirection0, int count, Float3 lightColor0, int dirLightCount, Float3 lightDirection1, float item6, Float3 lightColor1, float directionalBlockerDistance, Float4 item9, float fadeScale, float fadeOffset, float item12, float item13)
        {
            this.lightDirection0 = lightDirection0;
            Count = count;
            this.lightColor0 = lightColor0;
            this.dirLightCount = dirLightCount;
            this.lightDirection1 = lightDirection1;
            Item6 = item6;
            this.lightColor1 = lightColor1;
            DirectionalBlockerDistance = directionalBlockerDistance;
            Item9 = item9;
            this.fadeScale = fadeScale;
            this.fadeOffset = fadeOffset;
            Item12 = item12;
            Item13 = item13;
        }
    }
}