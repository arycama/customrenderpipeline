using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using static Math;

public partial class LightingSetup : CameraRenderFeature
{
	private readonly LightingSettings settings;

	public LightingSetup(RenderGraph renderGraph, LightingSettings settings) : base(renderGraph)
	{
		this.settings = settings;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;
		var directionalShadowRequests = ListPool<ShadowRequest>.Get();
		var pointShadowRequests = ListPool<ShadowRequest>.Get();
		var spotShadowRequests = ListPool<ShadowRequest>.Get();
		var lightList = ListPool<LightData>.Get();
		var directionalShadowMatrices = ListPool<Float4x4>.Get();

		// Find first 2 directional lights
		Float3 lightColor0 = Float3.Zero, lightColor1 = Float3.Zero, lightDirection0 = Float3.Up, lightDirection1 = Float3.Up;
		var dirLightCount = 0;

		var tanHalfFov = camera.TanHalfFov();
		var cameraTransform = camera.transform.WorldRigidTransform();
		var cameraToWorld = camera.transform.localToWorldMatrix;
		var cameraInverseTranslation = Matrix4x4.Translate(camera.transform.position);

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
					if (camera.cameraType == CameraType.SceneView && !UnityEditor.SceneView.currentDrawingSceneView.sceneLighting)
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

#if UNITY_EDITOR
			var size = light.areaSize;
#else
			// TODO: Fix
			var size = new Vector2(0.1f, 0.1f);
#endif

			if (light.shadows != LightShadows.None)
			{
				var hasShadowBounds = cullingResults.GetShadowCasterBounds(i, out var shadowCasterBounds);
				//var worldShadowBounds = (Bounds)shadowCasterBounds;
				//var viewBounds = worldShadowBounds.Transform3x4(camera.transform.worldToLocalMatrix);

				if (light.type == LightType.Directional)
				{
					// ref https://developer.nvidia.com/gpugems/gpugems3/part-ii-light-and-shadows/chapter-10-parallel-split-shadow-maps-programmable-gpus
					var lightInverse = ((Quaternion)visibleLight.light.transform.rotation).Inverse;
					var lightViewMatrix = Matrix4x4.Rotate(lightInverse);


					//var n = Max(camera.nearClipPlane, viewBounds.Min.z);
					//var f = Min(settings.DirectionalShadowDistance, viewBounds.Max.z);
					var n = camera.nearClipPlane;
					var f = settings.DirectionalShadowDistance;
					var m = (float)settings.DirectionalCascadeCount;
					var uniformity = Math.Pow(settings.CascadeUniformity, 2.2f);

					var lightView = lightInverse.Rotate(camera.transform.position);

					var viewPosition = camera.transform.position;
					var viewToWorld = (Float4x4)camera.transform.localToWorldMatrix;
					var viewRight = camera.transform.right;
					var viewUp = camera.transform.up;
					var viewForward = camera.transform.forward;

					var lightForward = light.transform.forward;

					//var lightViewMatrix = new Float4x4(light.transform.WorldRotation().Inverse);

					if (settings.UseCloseFit)
					{
						var up = Float3.Cross(lightForward, viewForward);
						lightViewMatrix = new Float4x4(Quaternion.LookRotation(light.transform.forward, up).Inverse);
					}

					var viewToLight = lightViewMatrix * camera.transform.localToWorldMatrix;

					//var viewToLight = lightViewMatrix.Mul(viewToWorld);

					Float3 GetCorner(FrustumCorner corner) => viewToLight.MultiplyPoint3x4(Geometry.GetFrustumCorner(tanHalfFov, camera.aspect, n, f, corner));

					var bottomLeftNear = GetCorner(FrustumCorner.BottomLeftNear);
					var topLeftNear = GetCorner(FrustumCorner.TopLeftNear);
					var topRightNear = GetCorner(FrustumCorner.TopRightNear);
					var bottomRightNear = GetCorner(FrustumCorner.BottomRightNear);
					var bottomLeftFar = GetCorner(FrustumCorner.BottomLeftFar);
					var topLeftFar = GetCorner(FrustumCorner.TopLeftFar);
					var topRightFar = GetCorner(FrustumCorner.TopRightFar);
					var bottomRightFar = GetCorner(FrustumCorner.BottomRightFar);

					var bottomLeft = new Line2D(bottomLeftNear.xy, bottomLeftFar.xy);
					var topLeft = new Line2D(topLeftNear.xy, topLeftFar.xy);
					var topRight = new Line2D(topRightNear.xy, topRightFar.xy);
					var bottomRight = new Line2D(bottomRightNear.xy, bottomRightFar.xy);

					var bottomLeftRay = new Ray2D(bottomLeftNear.xy, Float2.Normalize(bottomLeftFar.xy - bottomLeftNear.xy));
					var topLeftRay = new Ray2D(topLeftNear.xy, Float2.Normalize(topLeftFar.xy - topLeftNear.xy));
					var topRightRay = new Ray2D(topRightNear.xy, Float2.Normalize(topRightFar.xy - topRightNear.xy));
					var bottomRightRay = new Ray2D(bottomRightNear.xy, Float2.Normalize(bottomRightFar.xy - bottomRightNear.xy));

					float GetFrustumNear(int cascade)
					{
						var j = cascade;
						var logarithmicNear = j == 0 ? n : n * Pow(f / n, j / m);
						var uniformNear = n + (f - n) * (j / m);
						return Lerp(logarithmicNear, uniformNear, uniformity);
					}

					float GetFrustumFar(int cascade)
					{
						var j = cascade;
						var logarithmicFar = j + 1 == m ? f : n * Pow(f / n, (j + 1) / m);
						var uniformFar = n + (f - n) * ((j + 1) / m);
						return Lerp(logarithmicFar, uniformFar, uniformity);
					}

					Bounds GetCascadeBounds(int cascade)
					{
						var near = GetFrustumNear(cascade);
						var far = GetFrustumFar(cascade);
						return Geometry.GetFrustumBounds(tanHalfFov, camera.aspect, near, far, viewToLight);
					}

					// Move the start of the cascade forwards to avoid overlap
					// However, also ensure it does not get pushed beyond the edge of the current boundary to avoid too much squishing in oblique cases
					var boundsLimit = GetCascadeBounds(settings.DirectionalCascadeCount);

					Bounds GetAdjustedBounds(int cascade)
					{
						var j = cascade;
						var bounds = GetCascadeBounds(j);
						if (j > 0)
						{
							var previousBounds = GetCascadeBounds(j - 1);
							var nextBounds = GetCascadeBounds(j + 1);
							if (previousBounds.Max.x > bounds.Min.x)
							{
								// Only apply if this bounds start is behind the next bounds start. This will be true outside of oblique cases
								if (bounds.Min.x < boundsLimit.Min.x)
								{
									var minValue = Min(nextBounds.Min.x, Min(previousBounds.Max.x, boundsLimit.Min.x));
									bounds.Min = new Float3(minValue, bounds.Min.yz);

								}
							}
						}

						return bounds;
					}

					for (var j = 0; j < settings.DirectionalCascadeCount; j++)
					{
						// Transform camera split bounds to light space
						var logarithmicNear = j == 0 ? n : n * Pow(f / n, j / m);
						var uniformNear = n + (f - n) * (j / m);
						var near = Lerp(logarithmicNear, uniformNear, uniformity);

						var logarithmicFar = j + 1 == m ? f : n * Pow(f / n, (j + 1) / m);
						var uniformFar = n + (f - n) * ((j + 1) / m);
						var far = Lerp(logarithmicFar, uniformFar, uniformity);

						var viewLightBounds = Geometry.GetFrustumBounds(tanHalfFov, camera.aspect, near, far, viewToLight);
						var cascadeViewMatrix = lightViewMatrix;

						var bounds = GetAdjustedBounds(j);
						if (settings.UseOverlapFix)
						{
							if (bounds.Min.x < boundsLimit.Min.x)
							{
								// We need to expand the segment height to cover the newly visible region. To do this, Get the distance from each segment corner of the view frustum to the middle point and compute the new min max
								if (j < settings.DirectionalCascadeCount - 1)
								{
									// While current bounds max and next bounds min should generally be the same, there are cases where the current bounds max is limited, but we don't want to extend more than neccessary
									// So use the next bounds min as the max width to keep it conservative
									var nextAdjustedBounds = GetAdjustedBounds(j + 1);
									// Find out where the segments intersect our current max value.. 
									var currentLine = new Line2D(new Float2(nextAdjustedBounds.Min.x, 0), new Float2(nextAdjustedBounds.Min.x, 1));

									var hit0 = currentLine.IntersectLine(bottomLeft);
									var hit1 = currentLine.IntersectLine(topLeft);
									var hit2 = currentLine.IntersectLine(topRight);
									var hit3 = currentLine.IntersectLine(bottomRight);

									var minY = Min(Min(hit0.y, hit1.y), Min(hit2.y, hit3.y));
									var maxY = Max(Max(hit0.y, hit1.y), Max(hit2.y, hit3.y));

									// Clamp min max to not be smaller than original
									minY = Min(minY, bounds.Min.y);
									maxY = Max(maxY, bounds.Max.y);

									bounds.center.y = 0.5f * (maxY + minY);
									bounds.extents.y = 0.5f * (maxY - minY);
								}
							}

							// Need to calculate the min/max depth of each cascade. To do this, find where the four frustum segments intersect our max plane and take the max depth
							var minLine = new Line2D(new Float2(bounds.Min.x, 0), new Float2(bounds.Min.x, 1));
							var maxLine = new Line2D(new Float2(bounds.Max.x, 0), new Float2(bounds.Max.x, 1));

							var t0 = Max(minLine.DistanceAlongRay(bottomLeftRay), maxLine.DistanceAlongRay(bottomLeftRay)) / Float2.Distance(bottomLeftNear.xy, bottomLeftFar.xy);
							var t1 = Max(minLine.DistanceAlongRay(topLeftRay), maxLine.DistanceAlongRay(topLeftRay)) / Float2.Distance(topLeftNear.xy, topLeftFar.xy);
							var t2 = Max(minLine.DistanceAlongRay(topRightRay), maxLine.DistanceAlongRay(topRightRay)) / Float2.Distance(topRightNear.xy, topRightFar.xy);
							var t3 = Max(minLine.DistanceAlongRay(bottomRightRay), maxLine.DistanceAlongRay(bottomRightRay)) / Float2.Distance(bottomRightNear.xy, bottomRightFar.xy);

							var z0 = Lerp(bottomLeftNear.z, bottomLeftFar.z, t0);
							var z1 = Lerp(topLeftNear.z, topLeftFar.z, t1);
							var z2 = Lerp(topRightNear.z, topRightFar.z, t2);
							var z3 = Lerp(bottomRightNear.z, bottomRightFar.z, t3);

							var minZ = Min(Min(z0, z1), Min(z2, z3));
							var maxZ = Max(Max(z0, z1), Max(z2, z3));

							// Also test against the 8 frustum corners if they are within the bounds. 
							var rect = new Rect(bounds.Min.xy, bounds.Size.xy);

							void CheckCorner(Float3 corner)
							{
								if (rect.Contains(corner.xy))
								{
									minZ = Min(minZ, corner.z);
									maxZ = Max(maxZ, corner.z);
								}
							}

							CheckCorner(bottomLeftNear);
							CheckCorner(topLeftNear);
							CheckCorner(topRightNear);
							CheckCorner(bottomRightNear);
							CheckCorner(bottomLeftFar);
							CheckCorner(topLeftFar);
							CheckCorner(topRightFar);
							CheckCorner(bottomRightFar);

							bounds.center.z = 0.5f * (maxZ + minZ);
							bounds.extents.z = 0.5f * (maxZ - minZ);

							viewLightBounds = bounds;

							//projectionMatrix = Float4x4.Ortho(bounds);
							//var viewProjection = projectionMatrix.Mul(lightViewMatrix);
						}

						// Further limit shadow bounds to the size of the shadow bounds in cascade space
						//var cascadeBounds = worldShadowBounds.Transform3x4(cascadeViewMatrix);
						//viewLightBounds = viewLightBounds.Shrink(cascadeBounds);

						// Snap to texels to avoid shimmering
						if (settings.SnapTexels)
						{
							var worldUnitsPerTexel = viewLightBounds.Size.xy / settings.DirectionalShadowResolution;
							viewLightBounds.center.x = Floor(viewLightBounds.center.x / worldUnitsPerTexel.x) * worldUnitsPerTexel.x;
							viewLightBounds.center.y = Floor(viewLightBounds.center.y / worldUnitsPerTexel.y) * worldUnitsPerTexel.y;
						}

						var projectionMatrix = Float4x4.Ortho(viewLightBounds);
						var shadowSplitData = CalculateShadowSplitData(projectionMatrix * cascadeViewMatrix, visibleLight.localToWorldMatrix.Forward(), camera, true);
						shadowSplitData.shadowCascadeBlendCullingFactor = 1;

						var relativeViewMatrix = cascadeViewMatrix * cameraInverseTranslation;

						directionalShadowRequests.Add(new(i, relativeViewMatrix, projectionMatrix, shadowSplitData, -1, Float3.Zero, hasShadowBounds));
						directionalShadowMatrices.Add(MatrixExtensions.ConvertToAtlasMatrix(projectionMatrix * relativeViewMatrix));
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
						var shadowSplitData = CalculateShadowSplitData(viewProjectionMatrix, forward, camera, true);
						var viewMatrixRws = Matrix4x4Extensions.WorldToLocal(light.transform.position - camera.transform.position, rotation);
						viewMatrixRws.SetRow(1, -viewMatrixRws.GetRow(1));

						pointShadowRequests.Add(new(i, viewMatrixRws, projectionMatrix, shadowSplitData, index, light.transform.position, hasShadowBounds));
					}
				}

				if (visibleLight.lightType == LightType.Spot)
				{
					var forward = light.transform.forward;
					var viewMatrix = Matrix4x4Extensions.WorldToLocal(light.transform.position, light.transform.rotation);
					var projectionMatrix = Float4x4.Perspective(light.spotAngle, size.x / size.y, light.shadowNearPlane, light.range);

					var viewProjectionMatrix = projectionMatrix * viewMatrix;
					var shadowSplitData = CalculateShadowSplitData(viewProjectionMatrix, forward, camera, true);
					var viewMatrixRws = Matrix4x4Extensions.WorldToLocal(light.transform.position - camera.transform.position, light.transform.rotation);

					shadowIndex = (uint)spotShadowRequests.Count;
					var shadowRequest = new ShadowRequest(i, viewMatrixRws, projectionMatrix, shadowSplitData, -1, light.transform.position, hasShadowBounds);
					spotShadowRequests.Add(shadowRequest);
				}
			}

			uint lightType;
			switch (light.type)
			{
				case LightType.Directional:
					lightType = 0;
					break;
				case LightType.Point:
					lightType = 1;
					break;
				case LightType.Spot:
					lightType = 2;
					var halfAngle = Radians(light.spotAngle) * 0.5f;
					var innerConePercent = light.innerSpotAngle / visibleLight.spotAngle;
					var cosSpotOuterHalfAngle = Saturate(Cos(halfAngle));
					var cosSpotInnerHalfAngle = Saturate(Cos(halfAngle * innerConePercent));
					angleScale = Rcp(Max(1e-4f, cosSpotInnerHalfAngle - cosSpotOuterHalfAngle));
					angleOffset = -cosSpotOuterHalfAngle * angleScale;
					size.x = size.y = Rcp(Tan(halfAngle));
					break;
				case LightType.Pyramid:
					lightType = 3;
					break;
				case LightType.Box:
					lightType = 4;
					break;
				case LightType.Rectangle:
					lightType = 5;
					break;
				case LightType.Tube:
					lightType = 6;
					break;
				case LightType.Disc:
					lightType = 8;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(light.type));
			}

			var lightToWorld = visibleLight.localToWorldMatrix;
			var pointLightData = new LightData(
				lightToWorld.GetPosition() - camera.transform.position,
				light.range,
				(Vector4)visibleLight.finalColor,
				lightType,
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
		var directionalShadowMatricesBuffer = renderGraph.GetBuffer(Max(1, directionalShadowMatrices.Count), UnsafeUtility.SizeOf<Matrix4x4>());
		using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Set Directional Shadow Matrices"))
		{
			pass.WriteBuffer("", directionalShadowMatricesBuffer);
			pass.SetRenderFunction((directionalShadowMatrices, directionalShadowMatricesBuffer), static (command, pass, data) =>
			{
				command.SetBufferData(pass.GetBuffer(data.directionalShadowMatricesBuffer), data.directionalShadowMatrices);
				ListPool<Float4x4>.Release(data.directionalShadowMatrices);
			});
		}

		var lightingData = renderGraph.SetConstantBuffer
		((
			lightDirection0,
			directionalShadowRequests.Count,
			lightColor0,
			dirLightCount,
			lightDirection1,
			0,
			lightColor1,
			0
		));

		renderGraph.SetResource(new LightingData(lightDirection0, lightColor0, lightDirection1, lightColor1, lightingData, directionalShadowMatricesBuffer));

		var pointLightBuffer = lightList.Count == 0 ? renderGraph.EmptyBuffer : renderGraph.GetBuffer(lightList.Count, UnsafeUtility.SizeOf<LightData>());
		using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Set Light Data"))
		{
			pass.WriteBuffer("", pointLightBuffer);

			pass.SetRenderFunction(
			(
				lightList,
				pointLightBuffer
			),
			(command, pass, data) =>
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
