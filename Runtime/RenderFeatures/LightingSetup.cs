using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using static Math;

public partial class LightingSetup : ViewRenderFeature
{
	private readonly LightingSettings settings;
    private readonly NativeList<LightShadowCasterCullingInfo> perLightInfos = new(1, Allocator.Persistent);
    private readonly NativeList<ShadowSplitData> splitBuffer = new(1, Allocator.Persistent);

    public LightingSetup(RenderGraph renderGraph, LightingSettings settings) : base(renderGraph)
	{
		this.settings = settings;
	}

    protected override void Cleanup(bool disposing)
    {
        perLightInfos.Dispose();
        splitBuffer.Dispose();
    }

    public override void Render(ViewRenderData viewRenderData)
    {
		var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;
		var directionalShadowRequests = ListPool<ShadowRequest>.Get();
		var pointShadowRequests = ListPool<ShadowRequest>.Get();
		var spotShadowRequests = ListPool<ShadowRequest>.Get();
		var lightList = ListPool<LightData>.Get();
		var directionalShadowMatrices = ListPool<Float3x4>.Get();
		var directionalCascadeSizes = ListPool<Float4>.Get();

        // Find first 2 directional lights
        Float3 lightColor0 = Float3.Zero, lightColor1 = Float3.Zero;
        Quaternion lightRotation0 = Quaternion.Identity, lightRotation1 = Quaternion.Identity;
		var dirLightCount = 0;

		var n = viewRenderData.near;
		var f = settings.DirectionalShadowDistance;
		var m = (float)settings.DirectionalCascadeCount;
		var c = Pow(Max(1e-3f, settings.CascadeUniformity), 2.2f);

        perLightInfos.Clear();
        splitBuffer.Clear();

        for (var i = 0; i < cullingResults.visibleLights.Length; i++)
		{
            var visibleLight = cullingResults.visibleLights[i];
            var lightToWorld = (Float4x4)visibleLight.localToWorldMatrix;
            var lightPosition = lightToWorld.Translation;
            var lightRotation = lightToWorld.Rotation;

            if (visibleLight.lightType == LightType.Directional)
            {
                dirLightCount++;
                if (dirLightCount == 1)
                {
                    lightRotation0 = lightRotation;
                    lightColor0 = ColorspaceUtility.Rec709ToRec2020(visibleLight.finalColor.Float3());

                    #if UNITY_EDITOR
                        // The default scene light only has an intensity of 1, set it to sun
                        if (viewRenderData.camera.cameraType == CameraType.SceneView && !UnityEditor.SceneView.currentDrawingSceneView.sceneLighting || viewRenderData.camera.cameraType == CameraType.Preview)
                            lightColor0 *= 120000;
                    #endif
                }
                else if (dirLightCount < 3)
                {
                    lightRotation1 = lightRotation;
                    lightColor1 = ColorspaceUtility.Rec709ToRec2020(visibleLight.finalColor.Float3());
                }
            }

            // TODO: May need adjusting for spot lights?
            var angleScale = 0f;
            var angleOffset = 1f;
            var light = visibleLight.light;
            var shadowIndex = uint.MaxValue;

            BatchCullingProjectionType projectionType;
            ushort splitExclusionMask = 0;
            var splitRange = new RangeInt(0, 0);

            var size = light.areaSize;
			if (light.shadows != LightShadows.None)
			{
				var hasShadowBounds = cullingResults.GetShadowCasterBounds(i, out var shadowCasterBounds);

				if (light.type == LightType.Directional)
				{
                    // ref https://developer.nvidia.com/gpugems/gpugems3/part-ii-light-and-shadows/chapter-10-parallel-split-shadow-maps-programmable-gpus
                    var worldToView = Float4x4.Rotate(lightRotation.Inverse);
                    var cameraToView = worldToView.Mul(viewRenderData.camera.transform.localToWorldMatrix);

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
                        var viewBounds = Geometry.GetFrustumBounds(viewRenderData.tanHalfFov, near, far, cameraToView);
                        var viewToClip = Float4x4.OrthoReverseZ(viewBounds);
                        var shadowSplitData = CalculateShadowSplitData(viewToClip.Mul(worldToView), lightRotation.Forward, true);

                        var cameraInverseTranslation = Float4x4.Translate(viewRenderData.camera.transform.position);
                        var worldToCascade = worldToView.Mul(cameraInverseTranslation);

						var worldViewPosition = viewBounds.center;
						worldViewPosition.z = viewBounds.Min.z;
						worldViewPosition = lightToWorld.MultiplyPoint3x4(worldViewPosition);

						directionalShadowRequests.Add(new(i, worldToCascade, viewToClip, shadowSplitData, -1, Float3.Zero, hasShadowBounds, 0, viewBounds.Size.z, worldViewPosition, lightRotation, viewBounds.Size.x, viewBounds.Size.y, settings.DirectionalShadowResolution));
						directionalShadowMatrices.Add((Float3x4)Float4x4.OrthoReverseZSample(viewBounds).Mul(worldToCascade));

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
                        var cameraInverseTranslation = Float4x4.Translate(viewRenderData.camera.transform.position);
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
                    var cameraInverseTranslation = Float4x4.Translate(viewRenderData.camera.transform.position);
                    worldToView = worldToView.Mul(cameraInverseTranslation);

					shadowIndex = (uint)spotShadowRequests.Count;
					var shadowRequest = new ShadowRequest(i, worldToView, viewToClip, shadowSplitData, -1, lightPosition, hasShadowBounds, light.shadowNearPlane, light.range, lightPosition, lightRotation, light.spotAngle, size.x / size.y, settings.SpotShadowResolution);
					spotShadowRequests.Add(shadowRequest);

                    splitRange = new RangeInt(splitBuffer.Length, 1);
                    splitBuffer.Add(shadowSplitData);
                }
            }

			switch (light.type)
			{
				case LightType.Directional:
                    projectionType = BatchCullingProjectionType.Orthographic;
                    break;
				case LightType.Point:
                    projectionType = BatchCullingProjectionType.Perspective;
                    break;
				case LightType.Spot:
					var halfAngle = Radians(light.spotAngle) * 0.5f;
					var innerConePercent = light.innerSpotAngle / visibleLight.spotAngle;
					var cosSpotOuterHalfAngle = Saturate(Cos(halfAngle));
					var cosSpotInnerHalfAngle = Saturate(Cos(halfAngle * innerConePercent));
					angleScale = Rcp(Max(1e-4f, cosSpotInnerHalfAngle - cosSpotOuterHalfAngle));
					angleOffset = -cosSpotOuterHalfAngle * angleScale;
					size.x = size.y = Rcp(Tan(halfAngle));
                    projectionType = BatchCullingProjectionType.Perspective;
                    break;
				case LightType.Pyramid:
                    projectionType = BatchCullingProjectionType.Perspective;
                    break;
				case LightType.Box:
                    projectionType = BatchCullingProjectionType.Orthographic;
                    break;
				case LightType.Rectangle:
                    projectionType = BatchCullingProjectionType.Perspective;
                    break;
				case LightType.Tube:
                    projectionType = BatchCullingProjectionType.Perspective;
                    break;
				case LightType.Disc:
                    projectionType = BatchCullingProjectionType.Perspective;
                    break;
				default:
					throw new ArgumentOutOfRangeException(nameof(light.type));
			}

			var pointLightData = new LightData(
				lightToWorld.Translation - viewRenderData.transform.position,
				light.range,
				ColorspaceUtility.Rec709ToRec2020(visibleLight.finalColor.Float3()),
				(uint)light.type,
				lightToWorld.Right,
				angleScale,
				lightToWorld.Up,
				angleOffset,
				lightToWorld.Forward,
				shadowIndex,
				size,
				1.0f + light.range / (light.shadowNearPlane - light.range),
				light.shadowNearPlane * light.range / (light.range - light.shadowNearPlane));
			lightList.Add(pointLightData);

            var lightShadowCasterCullingInfo = new LightShadowCasterCullingInfo()
            {
                projectionType = projectionType,
                splitExclusionMask = splitExclusionMask,
                splitRange = splitRange
            };

            perLightInfos.Add(lightShadowCasterCullingInfo);
        }

        var infos = new ShadowCastersCullingInfos()
        {
            perLightInfos = perLightInfos.AsArray(),
            splitBuffer = splitBuffer.AsArray()
        };

        viewRenderData.context.CullShadowCasters(cullingResults, infos);

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

		var pointLightBuffer = lightList.Count == 0 ? renderGraph.EmptyBuffer : renderGraph.GetBuffer(lightList.Count, UnsafeUtility.SizeOf<LightData>());
		using (var pass = renderGraph.AddGenericRenderPass("Set Light Data", (lightList, pointLightBuffer)))
		{
			pass.WriteBuffer("", pointLightBuffer);
			pass.SetRenderFunction(static (command, pass, data) =>
			{
				command.SetBufferData(pass.GetBuffer(data.pointLightBuffer), data.lightList);
				ListPool<LightData>.Release(data.lightList);
			});
		}

		renderGraph.SetResource(new Result(pointLightBuffer, lightList.Count));
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
			if (plane.xyz.Dot(forward) < 0.0f)
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