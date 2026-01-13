using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using static Math;

public partial class LightingSetup : ViewRenderFeature
{
	private readonly LightingSettings settings;

	public LightingSetup(RenderGraph renderGraph, LightingSettings settings) : base(renderGraph)
	{
		this.settings = settings;
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
		Float3 lightColor0 = Float3.Zero, lightColor1 = Float3.Zero, lightDirection0 = Float3.Up, lightDirection1 = Float3.Up;
		var dirLightCount = 0;

		var cameraTransform = viewRenderData.transform;
		var cameraToWorld = viewRenderData.camera.transform.localToWorldMatrix;
		var cameraInverseTranslation = Matrix4x4.Translate(viewRenderData.camera.transform.position);

		var n = viewRenderData.near;
		var f = settings.DirectionalShadowDistance;
		var m = (float)settings.DirectionalCascadeCount;
		var c = Pow(Max(1e-3f, settings.CascadeUniformity), 2.2f);

		for (var i = 0; i < cullingResults.visibleLights.Length; i++)
		{
			var visibleLight = cullingResults.visibleLights[i];
			var light = visibleLight.light;
			var shadowIndex = uint.MaxValue;

			if (visibleLight.lightType == LightType.Directional)
			{
				dirLightCount++;
				if (dirLightCount == 1)
				{
					lightDirection0 = -visibleLight.localToWorldMatrix.Forward();
					lightColor0 = visibleLight.finalColor.Float3();

#if UNITY_EDITOR
					// The default scene light only has an intensity of 1, set it to sun
					if ((viewRenderData.camera.cameraType == CameraType.SceneView && !UnityEditor.SceneView.currentDrawingSceneView.sceneLighting) || viewRenderData.camera.cameraType == CameraType.Preview)
						lightColor0 *= 120000;
#endif
				}
				else if (dirLightCount < 3)
				{
					lightDirection1 = -visibleLight.localToWorldMatrix.Forward();
					lightColor1 = visibleLight.finalColor.Float3();
				}
			}

			// TODO: May need adjusting for spot lights?
			var angleScale = 0f;
			var angleOffset = 1f;

			var size = light.areaSize;
			if (light.shadows != LightShadows.None)
			{
				var hasShadowBounds = cullingResults.GetShadowCasterBounds(i, out var shadowCasterBounds);

				if (light.type == LightType.Directional)
				{
					// ref https://developer.nvidia.com/gpugems/gpugems3/part-ii-light-and-shadows/chapter-10-parallel-split-shadow-maps-programmable-gpus
					var lightRotation = (Quaternion)visibleLight.light.transform.rotation;
					var lightInverse = lightRotation.Inverse;
					var lightViewMatrix = Matrix4x4.Rotate(lightInverse);
					var viewToLight = lightViewMatrix * viewRenderData.camera.transform.localToWorldMatrix;

					float GetFrustumDepth(int j)
					{
						var L = Rcp(c);
						var M = Log2(c * (f - n) + 1);
						var N = n - Rcp(c);
						var x = j / m;
						return L * Exp2(M * x) + N;
					}

					for (var j = 0; j < settings.DirectionalCascadeCount; j++)
					{
						// Transform camera split bounds to light space
						var near = GetFrustumDepth(j);
						var far = GetFrustumDepth(j + 1);

						var viewLightBounds = Geometry.GetFrustumBounds(viewRenderData.tanHalfFov, near, far, viewToLight);
						var cascadeViewMatrix = lightViewMatrix;

						// Snap to texels to avoid shimmering
						var worldUnitsPerTexel = viewLightBounds.Size.xy / settings.DirectionalShadowResolution;

						var projectionMatrix = Float4x4.Ortho(viewLightBounds);
						var shadowSplitData = CalculateShadowSplitData(projectionMatrix * cascadeViewMatrix, visibleLight.localToWorldMatrix.Forward(), viewRenderData.camera, true);
						shadowSplitData.shadowCascadeBlendCullingFactor = 1;

						var relativeViewMatrix = cascadeViewMatrix * cameraInverseTranslation;

						var worldViewPosition = viewLightBounds.center;
						worldViewPosition.z = viewLightBounds.Min.z;
						worldViewPosition = cascadeViewMatrix.inverse.MultiplyPoint3x4(worldViewPosition);
						var viewRotation = cascadeViewMatrix.inverse.rotation;

						directionalShadowRequests.Add(new(i, relativeViewMatrix, projectionMatrix, shadowSplitData, -1, Float3.Zero, hasShadowBounds, 0, viewLightBounds.Size.z, worldViewPosition, viewRotation, viewLightBounds.Size.x, viewLightBounds.Size.y));
						directionalShadowMatrices.Add((Float3x4)MatrixExtensions.ConvertToAtlasMatrix(projectionMatrix * relativeViewMatrix));

						// Note it could be max(cascadeTexelSize * 0.5, but this means we'd get no anti-aliasing on the min filter size)
						var filterSize = Float2.Max(worldUnitsPerTexel, settings.DirectionalBlockerDistance * Radians(settings.SunAngularDiameter) * 0.5f);
						var filterRadius = Float2.Min(settings.DirectionalMaxFilterSize, Float2.Ceil(filterSize * settings.DirectionalShadowResolution * 0.5f));
						var rcpFilterSize = worldUnitsPerTexel / filterSize;
						directionalCascadeSizes.Add(new Float4(rcpFilterSize, filterRadius));
					}
				}

				if (visibleLight.lightType == LightType.Point)
				{
					shadowIndex = (uint)pointShadowRequests.Count;

					for (var j = 0; j < 6; j++)
					{
						// To undo unity's builtin inverted culling for point shadows, swap the top/bottom faces of the cubemap and flip the y axis. (This must also be done in the shader)
						var index = j;
						if (j == 2) index = 3;
						else if (j == 3) index = 2;

						var forward = Matrix4x4Extensions.lookAtList[index];
						var rotation = Quaternion.LookRotation(forward, Matrix4x4Extensions.upVectorList[index]);
						var viewMatrix = Matrix4x4Extensions.WorldToLocal(light.transform.position, rotation);
						var projectionMatrix = Float4x4.Perspective(90, 1, light.shadowNearPlane, light.range);
						var viewProjectionMatrix = projectionMatrix * viewMatrix;
						var shadowSplitData = CalculateShadowSplitData(viewProjectionMatrix, forward, viewRenderData.camera, true);
						var viewMatrixRws = Matrix4x4Extensions.WorldToLocal(light.transform.position - viewRenderData.transform.position, rotation);
						viewMatrixRws.SetRow(1, -viewMatrixRws.GetRow(1));

						pointShadowRequests.Add(new(i, viewMatrixRws, projectionMatrix, shadowSplitData, index, light.transform.position, hasShadowBounds, light.shadowNearPlane, light.range, light.transform.position, light.transform.rotation, 90, 1));
					}
				}

				// TODO: Box/Pyramid/Area/Disc
				if (visibleLight.lightType == LightType.Spot)
				{
					var forward = light.transform.forward;
					var viewMatrix = Matrix4x4Extensions.WorldToLocal(light.transform.position, light.transform.rotation);
					var projectionMatrix = Float4x4.Perspective(light.spotAngle, size.x / size.y, light.shadowNearPlane, light.range);

					var viewProjectionMatrix = projectionMatrix * viewMatrix;
					var shadowSplitData = CalculateShadowSplitData(viewProjectionMatrix, forward, viewRenderData.camera, true);
					var viewMatrixRws = Matrix4x4Extensions.WorldToLocal(light.transform.position - viewRenderData.transform.position, light.transform.rotation);

					shadowIndex = (uint)spotShadowRequests.Count;
					var shadowRequest = new ShadowRequest(i, viewMatrixRws, projectionMatrix, shadowSplitData, -1, light.transform.position, hasShadowBounds, light.shadowNearPlane, light.range, light.transform.position, light.transform.rotation, light.spotAngle, size.x / size.y);
					spotShadowRequests.Add(shadowRequest);
				}
			}

			switch (light.type)
			{
				case LightType.Directional:
					break;
				case LightType.Point:
					break;
				case LightType.Spot:
					var halfAngle = Radians(light.spotAngle) * 0.5f;
					var innerConePercent = light.innerSpotAngle / visibleLight.spotAngle;
					var cosSpotOuterHalfAngle = Saturate(Cos(halfAngle));
					var cosSpotInnerHalfAngle = Saturate(Cos(halfAngle * innerConePercent));
					angleScale = Rcp(Max(1e-4f, cosSpotInnerHalfAngle - cosSpotOuterHalfAngle));
					angleOffset = -cosSpotOuterHalfAngle * angleScale;
					size.x = size.y = Rcp(Tan(halfAngle));
					break;
				case LightType.Pyramid:
					break;
				case LightType.Box:
					break;
				case LightType.Rectangle:
					break;
				case LightType.Tube:
					break;
				case LightType.Disc:
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(light.type));
			}

			var lightToWorld = visibleLight.localToWorldMatrix;
			var pointLightData = new LightData(
				lightToWorld.GetPosition() - viewRenderData.transform.position,
				light.range,
				(Vector4)visibleLight.finalColor,
				(uint)light.type,
				lightToWorld.Right(),
				angleScale,
				lightToWorld.Up(),
				angleOffset,
				lightToWorld.Forward(),
				shadowIndex,
				size,
				1.0f + light.range / (light.shadowNearPlane - light.range),
				light.shadowNearPlane * light.range / (light.range - light.shadowNearPlane));
			lightList.Add(pointLightData);
		}

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
			lightDirection0,
			directionalShadowRequests.Count,
			lightColor0,
			dirLightCount,
			lightDirection1,
			settings.DirectionalMaxFilterSize,
			lightColor1,
			settings.DirectionalBlockerDistance,
			new Float4(E, F, G, 0),
			fadeScale,
			fadeOffset,
			settings.DirectionalShadowResolution,
			Rcp(settings.DirectionalShadowResolution)
		));

		renderGraph.SetResource(new LightingData(lightDirection0, lightColor0, lightDirection1, lightColor1, lightingData, directionalShadowMatricesBuffer, directionalCascadeSizesBuffer));

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

	private ShadowSplitData CalculateShadowSplitData(Matrix4x4 viewProjectionMatrix, bool skipNearPlane)
	{
		var shadowSplitData = new ShadowSplitData();
		using var frustumPlaneScope = ArrayPool<Plane>.Get((int)FrustumPlane.Count, out var frustumPlanes);
		GeometryUtility.CalculateFrustumPlanes(viewProjectionMatrix, frustumPlanes);
		for (var i = FrustumPlane.Left; i < FrustumPlane.Count; i++)
		{
			if (!skipNearPlane || i != FrustumPlane.Near)
				shadowSplitData.SetCullingPlane(shadowSplitData.cullingPlaneCount++, frustumPlanes[(int)i]);
		}

		return shadowSplitData;
	}

	/// <summary> Add any planes that face away from the light direction. This avoids rendering shadowcasters that can never cast a visible shadow </summary>
	private ShadowSplitData CalculateShadowSplitData(Matrix4x4 viewProjectionMatrix, Float3 forward, Camera camera, bool skipNearPlane)
	{
		var shadowSplitData = CalculateShadowSplitData(viewProjectionMatrix, skipNearPlane);
		using var frustumPlaneScope = ArrayPool<Plane>.Get((int)FrustumPlane.Count, out var frustumPlanes);
		GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
		for (var i = FrustumPlane.Left; i < FrustumPlane.Count; i++)
		{
			var plane = frustumPlanes[(int)i];
			if (Vector3.Dot(plane.normal, forward) < 0.0f)
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

	public override bool Equals(object obj) => obj is LightingDataStruct other && lightDirection0.Equals(other.lightDirection0) && Count == other.Count && lightColor0.Equals(other.lightColor0) && dirLightCount == other.dirLightCount && lightDirection1.Equals(other.lightDirection1) && Item6 == other.Item6 && lightColor1.Equals(other.lightColor1) && DirectionalBlockerDistance == other.DirectionalBlockerDistance && EqualityComparer<Float4>.Default.Equals(Item9, other.Item9) && fadeScale == other.fadeScale && fadeOffset == other.fadeOffset && Item12 == other.Item12 && Item13 == other.Item13;

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(lightDirection0);
		hash.Add(Count);
		hash.Add(lightColor0);
		hash.Add(dirLightCount);
		hash.Add(lightDirection1);
		hash.Add(Item6);
		hash.Add(lightColor1);
		hash.Add(DirectionalBlockerDistance);
		hash.Add(Item9);
		hash.Add(fadeScale);
		hash.Add(fadeOffset);
		hash.Add(Item12);
		hash.Add(Item13);
		return hash.ToHashCode();
	}

	public void Deconstruct(out Float3 lightDirection0, out int count, out Float3 lightColor0, out int dirLightCount, out Float3 lightDirection1, out float item6, out Float3 lightColor1, out float directionalBlockerDistance, out Float4 item9, out float fadeScale, out float fadeOffset, out float item12, out float item13)
	{
		lightDirection0 = this.lightDirection0;
		count = Count;
		lightColor0 = this.lightColor0;
		dirLightCount = this.dirLightCount;
		lightDirection1 = this.lightDirection1;
		item6 = Item6;
		lightColor1 = this.lightColor1;
		directionalBlockerDistance = DirectionalBlockerDistance;
		item9 = Item9;
		fadeScale = this.fadeScale;
		fadeOffset = this.fadeOffset;
		item12 = Item12;
		item13 = Item13;
	}

	public static implicit operator (Float3 lightDirection0, int Count, Float3 lightColor0, int dirLightCount, Float3 lightDirection1, float, Float3 lightColor1, float DirectionalBlockerDistance, Float4, float fadeScale, float fadeOffset, float, float)(LightingDataStruct value) => (value.lightDirection0, value.Count, value.lightColor0, value.dirLightCount, value.lightDirection1, value.Item6, value.lightColor1, value.DirectionalBlockerDistance, value.Item9, value.fadeScale, value.fadeOffset, value.Item12, value.Item13);
	public static implicit operator LightingDataStruct((Float3 lightDirection0, int Count, Float3 lightColor0, int dirLightCount, Float3 lightDirection1, float, Float3 lightColor1, float DirectionalBlockerDistance, Float4, float fadeScale, float fadeOffset, float, float) value) => new LightingDataStruct(value.lightDirection0, value.Count, value.lightColor0, value.dirLightCount, value.lightDirection1, value.Item6, value.lightColor1, value.DirectionalBlockerDistance, value.Item9, value.fadeScale, value.fadeOffset, value.Item12, value.Item13);
}