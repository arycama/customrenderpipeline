using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using static Math;

public class ShadowRenderer : CameraRenderFeature
{
	private readonly LightingSettings settings;
	private readonly TerrainShadowRenderer terrainShadowRenderer;

	private static IndexedString directionalCascadeIds = new("Directional Cascade "),
		pointLightIds = new("Point Light "),
		SpotLightIds = new("Spot Light ");

	public ShadowRenderer(RenderGraph renderGraph, LightingSettings settings, TerrainShadowRenderer terrainShadowRenderer) : base(renderGraph)
	{
		this.settings = settings;
		this.terrainShadowRenderer = terrainShadowRenderer;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		using var renderShadowsScope = renderGraph.AddProfileScope("Render Shadows");

		// TODO: Allocate 1 big atlas
		// TODO: Use renderer lists to avoid allocating/rendering empty cascades
		var requestData = renderGraph.GetResource<ShadowRequestsData>();
		var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;

		// Allocate and clear shadow maps
		var directionalShadowCount = Max(1, requestData.DirectionalShadowRequests.Count);
		var directionalShadows = renderGraph.GetTexture(settings.DirectionalShadowResolution, settings.DirectionalShadowResolution, GraphicsFormat.D16_UNorm, directionalShadowCount, TextureDimension.Tex2DArray, isExactSize: true); // TODO: Should allow for different slices, but exact resolution

		var pointShadowCount = Max(1, requestData.PointShadowRequests.Count);
		var pointShadows = renderGraph.GetTexture(settings.PointShadowResolution, settings.PointShadowResolution, GraphicsFormat.D16_UNorm, pointShadowCount, TextureDimension.Tex2DArray);

		var spotShadowCount = Max(1, requestData.SpotShadowRequests.Count);
		var spotShadows = renderGraph.GetTexture(settings.SpotShadowResolution, settings.SpotShadowResolution, GraphicsFormat.D16_UNorm, spotShadowCount, TextureDimension.Tex2DArray);
		renderGraph.SetResource(new ShadowData(directionalShadows, pointShadows, spotShadows));

		using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Render Shadows Setup"))
		{
			// TODO: We should really add initial clear actions
			pass.WriteTexture(directionalShadows);
			pass.WriteTexture(pointShadows);
			pass.WriteTexture(spotShadows);

			pass.SetRenderFunction((directionalShadows, pointShadows, spotShadows), static (command, pass, data) =>
			{
				command.SetRenderTarget(pass.GetRenderTexture(data.directionalShadows), pass.GetRenderTexture(data.directionalShadows), 0, CubemapFace.Unknown, -1);
				command.ClearRenderTarget(true, false, Color.clear);

				command.SetRenderTarget(pass.GetRenderTexture(data.pointShadows), pass.GetRenderTexture(data.pointShadows), 0, CubemapFace.Unknown, -1);
				command.ClearRenderTarget(true, false, Color.clear);

				command.SetRenderTarget(pass.GetRenderTexture(data.spotShadows), pass.GetRenderTexture(data.spotShadows), 0, CubemapFace.Unknown, -1);
				command.ClearRenderTarget(true, false, Color.clear);
			});
		}

		void RenderShadowMap(ShadowRequest request, BatchCullingProjectionType projectionType, ResourceHandle<RenderTexture> target, int index, float bias, float slopeBias, bool flipY)
		{
			var viewToShadowClip = GL.GetGPUProjectionMatrix(request.ProjectionMatrix, flipY);
			var perCascadeData = renderGraph.SetConstantBuffer((request.ViewMatrix, viewToShadowClip * request.ViewMatrix, viewToShadowClip, camera.transform.position, 0, request.LightPosition, 0));
			renderGraph.SetResource(new ShadowRequestData(request, bias, slopeBias, target, index, perCascadeData));
			terrainShadowRenderer.Render(camera, context);

			using (var pass = renderGraph.AddRenderPass<ShadowRenderPass>("Render Shadow"))
			{
				var isOrtho = projectionType == BatchCullingProjectionType.Orthographic;
				pass.Initialize(context, cullingResults, request.LightIndex, projectionType, request.ShadowSplitData, bias, slopeBias, !isOrtho, !isOrtho);

				// Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
				pass.WriteTexture(target);
				pass.ReadBuffer("PerCascadeData", perCascadeData);

				pass.SetRenderFunction((target, index),
				static (command, pass, data) =>
				{
					command.SetRenderTarget(pass.GetRenderTexture(data.target), pass.GetRenderTexture(data.target), 0, CubemapFace.Unknown, data.index);
				});
			}
		}

		using (renderGraph.AddProfileScope($"Directional Shadows"))
		{
			for (var i = 0; i < requestData.DirectionalShadowRequests.Count; i++)
			{
				using (renderGraph.AddProfileScope(directionalCascadeIds.GetString(i)))
				{
					var request = requestData.DirectionalShadowRequests[i];
					RenderShadowMap(request, BatchCullingProjectionType.Orthographic, directionalShadows, i, settings.DirectionalShadowBias, settings.DirectionalShadowSlopeBias, true);
				}
			}

			ListPool<ShadowRequest>.Release(requestData.DirectionalShadowRequests);
		}

		using (renderGraph.AddProfileScope("Point Shadows"))
		{
			for (var i = 0; i < requestData.PointShadowRequests.Count; i++)
			{
				using (renderGraph.AddProfileScope(pointLightIds.GetString(i)))
				{
					var request = requestData.PointShadowRequests[i];
					RenderShadowMap(request, BatchCullingProjectionType.Perspective, pointShadows, i, settings.PointShadowBias, settings.PointShadowSlopeBias, false);
				}
			}
			ListPool<ShadowRequest>.Release(requestData.PointShadowRequests);
		}

		using (renderGraph.AddProfileScope("Spot Shadows"))
		{
			for (var i = 0; i < requestData.SpotShadowRequests.Count; i++)
			{
				using (renderGraph.AddProfileScope(SpotLightIds.GetString(i)))
				{
					var request = requestData.SpotShadowRequests[i];
					RenderShadowMap(request, BatchCullingProjectionType.Perspective, spotShadows, i, settings.SpotShadowBias, settings.SpotShadowSlopeBias, true);
				}
			}

			ListPool<ShadowRequest>.Release(requestData.SpotShadowRequests);
		}
	}
}