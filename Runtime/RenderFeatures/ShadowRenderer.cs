using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using static Math;

public class ShadowRenderer : CameraRenderFeature
{
	private readonly LightingSettings settings;
	private readonly TerrainShadowRenderer terrainShadowRenderer;
	private readonly GpuDrivenRenderer gpuDrivenRenderer;

	private static IndexedString directionalCascadeIds = new("Directional Cascade "),
		pointLightIds = new("Point Light "),
		SpotLightIds = new("Spot Light ");

	public ShadowRenderer(RenderGraph renderGraph, LightingSettings settings, TerrainShadowRenderer terrainShadowRenderer, GpuDrivenRenderer gpuDrivenRenderer) : base(renderGraph)
	{
		this.settings = settings;
		this.terrainShadowRenderer = terrainShadowRenderer;
		this.gpuDrivenRenderer = gpuDrivenRenderer;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		using var renderShadowsScope = renderGraph.AddProfileScope("Render Shadows");

		// TODO: Allocate 1 big atlas
		// TODO: Use renderer lists to avoid allocating/rendering empty cascades
		var requestData = renderGraph.GetResource<ShadowRequestsData>();
		var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

		// Allocate and clear shadow maps
		var directionalShadowCount = Max(1, requestData.directionalShadowRequests.Count);
		var directionalShadows = renderGraph.GetTexture(settings.DirectionalShadowResolution, settings.DirectionalShadowResolution, GraphicsFormat.D16_UNorm, directionalShadowCount, TextureDimension.Tex2DArray, isExactSize: true);

		var pointShadowCount = Max(1, requestData.pointShadowRequests.Count);
		var pointShadows = renderGraph.GetTexture(settings.PointShadowResolution, settings.PointShadowResolution, GraphicsFormat.D16_UNorm, pointShadowCount, TextureDimension.Tex2DArray, isExactSize: true);

		var spotShadowCount = Max(1, requestData.spotShadowRequests.Count);
		var spotShadows = renderGraph.GetTexture(settings.SpotShadowResolution, settings.SpotShadowResolution, GraphicsFormat.D16_UNorm, spotShadowCount, TextureDimension.Tex2DArray, isExactSize: true);
		renderGraph.SetResource(new ShadowData(directionalShadows, pointShadows, spotShadows));

		using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Render Shadows Setup"))
		{
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

		void RenderShadowMap(ShadowRequest request, BatchCullingProjectionType projectionType, ResourceHandle<RenderTexture> target, int index, float bias, float slopeBias, bool flipY, bool zClip, bool isPointLight)
		{
			var viewToShadowClip = GL.GetGPUProjectionMatrix(request.ProjectionMatrix, flipY);
			var perCascadeData = renderGraph.SetConstantBuffer((request.ViewMatrix, viewToShadowClip * request.ViewMatrix, viewToShadowClip, camera.transform.position, 0, request.LightPosition, 0));
			var shadowRequestData = new ShadowRequestData(request, bias, slopeBias, target, index, perCascadeData, zClip);
			renderGraph.SetResource(shadowRequestData);
			terrainShadowRenderer.Render(camera, context);

			if (request.HasCasters)
			{
				using (var pass = renderGraph.AddRenderPass<ShadowRenderPass>("Render Shadow"))
				{
					pass.Initialize(context, cullingResults, request.LightIndex, projectionType, request.ShadowSplitData, bias, slopeBias, zClip, isPointLight);
					pass.DepthSlice = index;
					pass.WriteDepth(target);
					pass.AddRenderPassData<ShadowRequestData>();
				}
			}

			gpuDrivenRenderer.RenderShadow(camera.transform.position, shadowRequestData, camera.ScaledViewSize());
		}

		using (renderGraph.AddProfileScope($"Directional Shadows"))
		{
			for (var i = 0; i < requestData.directionalShadowRequests.Count; i++)
			{
				using (renderGraph.AddProfileScope(directionalCascadeIds[i]))
				{
					var request = requestData.directionalShadowRequests[i];
					RenderShadowMap(request, BatchCullingProjectionType.Orthographic, directionalShadows, i, settings.DirectionalShadowBias, settings.DirectionalShadowSlopeBias, true, false, false);
				}
			}

			// This is released via particle shadows instead
			//ListPool<ShadowRequest>.Release(requestData.DirectionalShadowRequests);
		}

		using (renderGraph.AddProfileScope("Point Shadows"))
		{
			for (var i = 0; i < requestData.pointShadowRequests.Count; i++)
			{
				using (renderGraph.AddProfileScope(pointLightIds[i]))
				{
					var request = requestData.pointShadowRequests[i];
					RenderShadowMap(request, BatchCullingProjectionType.Perspective, pointShadows, i, settings.PointShadowBias, settings.PointShadowSlopeBias, false, true, true);
				}
			}
			ListPool<ShadowRequest>.Release(requestData.pointShadowRequests);
		}

		using (renderGraph.AddProfileScope("Spot Shadows"))
		{
			for (var i = 0; i < requestData.spotShadowRequests.Count; i++)
			{
				using (renderGraph.AddProfileScope(SpotLightIds[i]))
				{
					var request = requestData.spotShadowRequests[i];
					RenderShadowMap(request, BatchCullingProjectionType.Perspective, spotShadows, i, settings.SpotShadowBias, settings.SpotShadowSlopeBias, true, true, false);
				}
			}

			ListPool<ShadowRequest>.Release(requestData.spotShadowRequests);
		}
	}
}