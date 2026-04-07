using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using static Math;

public class ShadowRenderer : ViewRenderFeature
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

	public override void Render(ViewRenderData viewRenderData)
    {
		using var renderShadowsScope = renderGraph.AddProfileScope("Render Shadows");

		// TODO: Allocate 1 big atlas
		// TODO: Use renderer lists to avoid allocating/rendering empty cascades
		var requestData = renderGraph.GetResource<ShadowRequestsData>();
		var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

		var directionalShadowCount = Max(1, requestData.directionalShadowRequests.Count);
		var directionalShadows = renderGraph.GetTexture(settings.DirectionalShadowResolution, settings.Use32Bit ? GraphicsFormat.D32_SFloat : GraphicsFormat.D16_UNorm, directionalShadowCount, TextureDimension.Tex2DArray, isExactSize: true);
		using (renderGraph.AddProfileScope($"Directional Shadows"))
		{
            using (var pass = renderGraph.AddGenericRenderPass("Render Shadows Setup", directionalShadows))
            {
                pass.WriteTexture(directionalShadows);

                if (requestData.directionalShadowRequests.Count > 0)
                {
                    pass.SetRenderFunction(static (command, pass, directionalShadows) =>
                    {
                        command.SetRenderTarget(pass.GetRenderTexture(directionalShadows), 0, CubemapFace.Unknown, -1);
                        command.ClearRenderTarget(true, false, default);
                    });
                }
            }

            for (var i = 0; i < requestData.directionalShadowRequests.Count; i++)
			{
				using (renderGraph.AddProfileScope(directionalCascadeIds[i]))
				{
					var request = requestData.directionalShadowRequests[i];
					RenderShadowMap(request, directionalShadows, i, settings.DirectionalShadowBias, settings.DirectionalShadowSlopeBias, true, false, false, viewRenderData, cullingResults, settings.DirectionalShadowResolution, settings.DirectionalCascadeCount);
				}
			}

			// This is released via particle shadows instead
			//ListPool<ShadowRequest>.Release(requestData.DirectionalShadowRequests);
		}

        var pointShadowCount = Max(1, requestData.pointShadowRequests.Count);
        var pointShadows = renderGraph.GetTexture(settings.PointShadowResolution, GraphicsFormat.D16_UNorm, pointShadowCount, TextureDimension.Tex2DArray, isExactSize: true);
        using (renderGraph.AddProfileScope("Point Shadows"))
		{
            using (var pass = renderGraph.AddGenericRenderPass("Render Shadows Setup", pointShadows))
            {
                pass.WriteTexture(pointShadows);

                if (requestData.directionalShadowRequests.Count > 0)
                {
                    pass.SetRenderFunction(static (command, pass, pointShadows) =>
                    {
                        command.SetRenderTarget(pass.GetRenderTexture(pointShadows), 0, CubemapFace.Unknown, -1);
                        command.ClearRenderTarget(true, false, default);
                    });
                }
            }

            for (var i = 0; i < requestData.pointShadowRequests.Count; i++)
			{
				using (renderGraph.AddProfileScope(pointLightIds[i]))
				{
					var request = requestData.pointShadowRequests[i];
                    RenderShadowMap(request, pointShadows, i, settings.PointShadowBias, settings.PointShadowSlopeBias, false, true, true, viewRenderData, cullingResults, settings.PointShadowResolution, pointShadowCount);
				}
			}
			ListPool<ShadowRequest>.Release(requestData.pointShadowRequests);
		}

        var spotShadowCount = Max(1, requestData.spotShadowRequests.Count);
        var spotShadows = renderGraph.GetTexture(settings.SpotShadowResolution, GraphicsFormat.D16_UNorm, spotShadowCount, TextureDimension.Tex2DArray, isExactSize: true);

        using (renderGraph.AddProfileScope("Spot Shadows"))
		{
            using (var pass = renderGraph.AddGenericRenderPass("Render Shadows Setup", spotShadows))
            {
                pass.WriteTexture(spotShadows);

                if (requestData.spotShadowRequests.Count > 0)
                {
                    pass.SetRenderFunction(static (command, pass, spotShadows) =>
                    {
                        command.SetRenderTarget(pass.GetRenderTexture(spotShadows), 0, CubemapFace.Unknown, -1);
                        command.ClearRenderTarget(true, false, default);
                    });
                }
            }

            for (var i = 0; i < requestData.spotShadowRequests.Count; i++)
			{
				using (renderGraph.AddProfileScope(SpotLightIds[i]))
				{
					var request = requestData.spotShadowRequests[i];
                    RenderShadowMap(request, spotShadows, i, settings.SpotShadowBias, settings.SpotShadowSlopeBias, true, true, false, viewRenderData, cullingResults, settings.SpotShadowResolution, spotShadowCount);
				}
			}

			ListPool<ShadowRequest>.Release(requestData.spotShadowRequests);
		}

        renderGraph.SetResource(new ShadowData(directionalShadows, pointShadows, spotShadows));
    }

    void RenderShadowMap(ShadowRequest request, ResourceHandle<RenderTexture> target, int index, float bias, float slopeBias, bool flipY, bool zClip, bool isPointLight, ViewRenderData viewRenderData, CullingResults cullingResults, Int2 resolution, int viewCount)
	{
		var viewToShadowClip = GL.GetGPUProjectionMatrix(request.ProjectionMatrix, flipY);
		var perCascadeData = renderGraph.SetConstantBuffer(new RenderShadowMapData(request.ViewMatrix, viewToShadowClip * request.ViewMatrix, viewToShadowClip, viewRenderData.transform.position, request.Near, request.LightPosition, request.Far, request.ViewRotation.Forward, 0));
		var shadowRequestData = new ShadowRequestData(request, bias, slopeBias, target, index, perCascadeData, zClip);

        renderGraph.SetResource(shadowRequestData);
        terrainShadowRenderer.Render(viewRenderData);
		if (request.HasCasters)
		{
			using (var pass = renderGraph.AddShadowRenderPass("Render Shadow"))
			{
				pass.Initialize(viewRenderData.context, cullingResults, request.LightIndex, bias, slopeBias, zClip, isPointLight, resolution, viewCount);
				pass.DepthSlice = index;
				pass.WriteDepth(target);
				pass.ReadResource<ShadowRequestData>();
			}
		}

		gpuDrivenRenderer.RenderShadow(viewRenderData.transform.position, shadowRequestData, viewRenderData.viewSize);
    }
}

internal struct RenderShadowMapData
{
    public Matrix4x4 ViewMatrix;
    public Matrix4x4 Item2;
    public Matrix4x4 viewToShadowClip;
    public Float3 position;
    public float Near;
    public Float3 LightPosition;
    public float Far;
    public Float3 Forward;
    public int Item9;

    public RenderShadowMapData(Matrix4x4 viewMatrix, Matrix4x4 item2, Matrix4x4 viewToShadowClip, Float3 position, float near, Float3 lightPosition, float far, Float3 forward, int item9)
    {
        ViewMatrix = viewMatrix;
        Item2 = item2;
        this.viewToShadowClip = viewToShadowClip;
        this.position = position;
        Near = near;
        LightPosition = lightPosition;
        Far = far;
        Forward = forward;
        Item9 = item9;
    }
}