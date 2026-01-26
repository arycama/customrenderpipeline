using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using static Math;
using Object = UnityEngine.Object;

public class ParticleShadows : ViewRenderFeature
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField] public int DirectionalResolution { get; private set; } = 256;
		[field: SerializeField] public int DirectionalDepth { get; private set; } = 64;
	}

	private static IndexedString directionalCascadeIds = new("Directional Cascade "),
		pointLightIds = new("Point Light "),
		SpotLightIds = new("Spot Light ");

	private readonly Settings settings;
	//private readonly Material material;
	private readonly ComputeShader accumulateShader;

	private readonly List<Camera> cameras = new();

	public ParticleShadows(RenderGraph renderGraph, Settings settings) : base(renderGraph)
	{
		this.settings = settings;
		//material = new Material(Shader.Find("")) { hideFlags = HideFlags.HideAndDontSave };
		accumulateShader = Resources.Load<ComputeShader>("Lighting/ParticleShadowAccumulate");
	}

	protected override void Cleanup(bool disposing)
	{
		foreach (var camera in cameras)
			Object.DestroyImmediate(camera.gameObject);

		cameras.Clear();
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		using var renderShadowsScope = renderGraph.AddProfileScope("Particle Shadows");

		// TODO: Allocate 1 big atlas
		// TODO: Use renderer lists to avoid allocating/rendering empty cascades
		var requestData = renderGraph.GetResource<ShadowRequestsData>();
		var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

		// Allocate and clear shadow maps
		var directionalShadowCount = Max(1, requestData.directionalShadowRequests.Count);

		// Since 3D texture arrays aren't a thing, allocate one wide texture
		var directionalShadows = renderGraph.GetTexture(new(settings.DirectionalResolution * directionalShadowCount, settings.DirectionalResolution), GraphicsFormat.R8_UNorm, settings.DirectionalDepth, TextureDimension.Tex3D, isExactSize: true, clear: true, clearColor: Color.white);

		void RenderShadowMap(ShadowRequest request, ResourceHandle<RenderTexture> target, int index, bool flipY, bool zClip, bool isPointLight)
		{
			var viewToShadowClip = GL.GetGPUProjectionMatrix(request.ProjectionMatrix, flipY);
			var perCascadeData = renderGraph.SetConstantBuffer((request.ViewMatrix, viewToShadowClip * request.ViewMatrix, viewToShadowClip, viewRenderData.transform.position, 0, request.LightPosition, 0));

			while (cameras.Count <= index)
			{
				// Each probe gets it's own camera. This seems to be neccessary for now
				// TODO: Try removing this after we convert the logic to use matrices instead of transforms, and see if we can just use one camera. 
				var cameraGameObject = new GameObject("Particle Shadow Camera")
				{
					hideFlags = HideFlags.HideAndDontSave
				};

				var particleShadowCamera = cameraGameObject.AddComponent<Camera>();
				particleShadowCamera.enabled = false;
				particleShadowCamera.orthographic = true;
				cameras.Add(particleShadowCamera);
			}

			var particleCamera = cameras[index];
			particleCamera.transform.position = request.ViewPosition;
			particleCamera.transform.rotation = request.ViewRotation;
			particleCamera.nearClipPlane = request.Near;
			particleCamera.farClipPlane = request.Far;
			particleCamera.orthographicSize = request.Height * 0.5f;
			particleCamera.aspect = request.Width / request.Height;

			if (!particleCamera.TryGetCullingParameters(out var cullingPrameters))
				return;

			var voxelSize = (request.Far - request.Near) / settings.DirectionalDepth;
			using (var pass = renderGraph.AddObjectRenderPass("Particle Shadows", (target, index, settings.DirectionalResolution, settings.DirectionalDepth, zClip, voxelSize)))
			{
				cullingPrameters.cullingOptions = CullingOptions.ForceEvenIfCameraIsNotActive | CullingOptions.DisablePerObjectCulling;
				var cullingResults = viewRenderData.context.Cull(ref cullingPrameters);
				pass.Initialize("ParticleShadow", viewRenderData.context, cullingResults, particleCamera, RenderQueueRange.transparent, SortingCriteria.CommonTransparent);

				// Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
				pass.WriteTexture(directionalShadows);
				pass.ReadTexture("ParticleShadowWrite", target);
				pass.ReadRtHandle<CameraDepth>();
				pass.ReadResource<ViewData>(); 

				pass.ReadBuffer("PerCascadeData", perCascadeData);

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					pass.SetFloat("_ZClip", data.zClip ? 1.0f : 0.0f);
					pass.SetFloat("ParticleShadowDepth", data.DirectionalDepth);
					pass.SetFloat("ParticleShadowResolution", data.DirectionalResolution);
					pass.SetFloat("ParticleShadowIndex", data.index);
					pass.SetFloat("ParticleVoxelSize", data.voxelSize);
					command.SetViewport(new Rect(data.index * data.DirectionalResolution, 0, data.DirectionalResolution, data.DirectionalResolution));
				});
			}
		}

		var directionalShadowSizes = ArrayPool<float>.Get(directionalShadowCount);
		using (renderGraph.AddProfileScope($"Directional Shadows"))
		{
			// TODO: This needs to use the actual count or it will access out of bounds
			for (var i = 0; i < requestData.directionalShadowRequests.Count; i++)
			{
				using (renderGraph.AddProfileScope(directionalCascadeIds[i]))
				{
					var request = requestData.directionalShadowRequests[i];
					RenderShadowMap(request, directionalShadows, i, true, false, false);
					directionalShadowSizes[i] = (request.Far - request.Near) / settings.DirectionalDepth;
				}
			}

			ListPool<ShadowRequest>.Release(requestData.directionalShadowRequests);
		}

		// Accumulate directional shadows
		var result = renderGraph.GetTexture(new(settings.DirectionalResolution * directionalShadowCount, settings.DirectionalResolution), GraphicsFormat.R8_UNorm, settings.DirectionalDepth, TextureDimension.Tex3D, isRandomWrite: true, isExactSize: true);
		var shadowSteps = renderGraph.GetBuffer(directionalShadowCount);

		using (var pass = renderGraph.AddComputeRenderPass("Particle Shadow Directional Accumulate", (settings.DirectionalDepth, settings.DirectionalResolution, shadowSteps, directionalShadowSizes)))
		{
			pass.Initialize(accumulateShader, 0, settings.DirectionalResolution * directionalShadowCount, settings.DirectionalResolution, 1);
			pass.ReadBuffer("ParticleShadowStep", shadowSteps);
			pass.WriteBuffer("ParticleShadowStep", shadowSteps);
			pass.WriteTexture("ParticleShadowWrite", result);
			pass.ReadTexture("ParticleExtinction", directionalShadows);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetInt("ParticleShadowDepthInt", data.DirectionalDepth);
				pass.SetInt("ParticleShadowResolutionInt", data.DirectionalResolution);
				command.SetBufferData(pass.GetBuffer(data.shadowSteps), data.directionalShadowSizes);
				ArrayPool<float>.Release(data.directionalShadowSizes);
			});
		}

		//using (renderGraph.AddProfileScope("Point Shadows"))
		//{
		//	for (var i = 0; i < requestData.PointShadowRequests.Count; i++)
		//	{
		//		using (renderGraph.AddProfileScope(pointLightIds[i]))
		//		{
		//			var request = requestData.PointShadowRequests[i];
		//			RenderShadowMap(request, BatchCullingProjectionType.Perspective, pointShadows, i, settings.PointShadowBias, settings.PointShadowSlopeBias, false, true, true);
		//		}
		//	}
		//	ListPool<ShadowRequest>.Release(requestData.PointShadowRequests);
		//}

		//using (renderGraph.AddProfileScope("Spot Shadows"))
		//{
		//	for (var i = 0; i < requestData.SpotShadowRequests.Count; i++)
		//	{
		//		using (renderGraph.AddProfileScope(SpotLightIds[i]))
		//		{
		//			var request = requestData.SpotShadowRequests[i];
		//			RenderShadowMap(request, BatchCullingProjectionType.Perspective, spotShadows, i, settings.SpotShadowBias, settings.SpotShadowSlopeBias, true, true, false);
		//		}
		//	}

		//	ListPool<ShadowRequest>.Release(requestData.SpotShadowRequests);
		//}

		renderGraph.SetResource(new ParticleShadowData(result/*, pointShadows, spotShadows*/));
	}
}
