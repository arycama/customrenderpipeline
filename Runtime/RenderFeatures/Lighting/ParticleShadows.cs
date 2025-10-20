using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using static Math;
using Object = UnityEngine.Object;

public class ParticleShadows : CameraRenderFeature
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

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		using var renderShadowsScope = renderGraph.AddProfileScope("Particle Shadows");

		// TODO: Allocate 1 big atlas
		// TODO: Use renderer lists to avoid allocating/rendering empty cascades
		var requestData = renderGraph.GetResource<ShadowRequestsData>();
		var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;

		// Allocate and clear shadow maps
		var directionalShadowCount = Max(1, requestData.DirectionalShadowRequests.Count);

		// Since 3D texture arrays aren't a thing, allocate one wide texture
		var directionalShadows = renderGraph.GetTexture(settings.DirectionalResolution * directionalShadowCount, settings.DirectionalResolution, GraphicsFormat.R32_UInt, settings.DirectionalDepth, TextureDimension.Tex3D, isRandomWrite: true, isExactSize: true, clearFlags: RTClearFlags.Depth);

		// We need a dummy texture, just use smallest format available
		var dummy = renderGraph.GetTexture(settings.DirectionalResolution, settings.DirectionalResolution, GraphicsFormat.R8_UNorm, isExactSize: true);

		//var pointShadowCount = Max(1, requestData.PointShadowRequests.Count);
		//var pointShadows = renderGraph.GetTexture(settings.PointShadowResolution, settings.PointShadowResolution, GraphicsFormat.D16_UNorm, pointShadowCount, TextureDimension.Tex2DArray, isExactSize: true);

		//var spotShadowCount = Max(1, requestData.SpotShadowRequests.Count);
		//var spotShadows = renderGraph.GetTexture(settings.SpotShadowResolution, settings.SpotShadowResolution, GraphicsFormat.D16_UNorm, spotShadowCount, TextureDimension.Tex2DArray, isExactSize: true);

		using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Render Shadows Setup"))
		{
			// TODO: We should really add initial clear actions
			pass.WriteTexture(directionalShadows);
			//pass.WriteTexture(pointShadows);
			//pass.WriteTexture(spotShadows);

			// Here to avoid crashes due to not being written if no dir shadows..
			//pass.WriteTexture(dummy);
			//pass.ReadTexture("", dummy);

			pass.SetRenderFunction((directionalShadows/*, pointShadows, spotShadows*/), static (command, pass, data) =>
			{
				command.SetRenderTarget(pass.GetRenderTexture(data), pass.GetRenderTexture(data), 0, CubemapFace.Unknown, -1);
				command.ClearRenderTarget(false, true, Color.clear);

				//command.SetRenderTarget(pass.GetRenderTexture(data.pointShadows), pass.GetRenderTexture(data.pointShadows), 0, CubemapFace.Unknown, -1);
				//command.ClearRenderTarget(true, false, Color.clear);

				//command.SetRenderTarget(pass.GetRenderTexture(data.spotShadows), pass.GetRenderTexture(data.spotShadows), 0, CubemapFace.Unknown, -1);
				//command.ClearRenderTarget(true, false, Color.clear);
			});
		}

		void RenderShadowMap(ShadowRequest request, ResourceHandle<RenderTexture> target, int index, bool flipY, bool zClip, bool isPointLight)
		{
			var viewToShadowClip = GL.GetGPUProjectionMatrix(request.ProjectionMatrix, flipY);
			var perCascadeData = renderGraph.SetConstantBuffer((request.ViewMatrix, viewToShadowClip * request.ViewMatrix, viewToShadowClip, camera.transform.position, 0, request.LightPosition, 0));

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

			using (var pass = renderGraph.AddRenderPass<ObjectRenderPass>("Particle Shadows"))
			{
				cullingPrameters.cullingOptions = CullingOptions.ForceEvenIfCameraIsNotActive | CullingOptions.DisablePerObjectCulling;
				var cullingResults = context.Cull(ref cullingPrameters);
				pass.Initialize("ParticleShadow", context, cullingResults, particleCamera, RenderQueueRange.transparent, SortingCriteria.CommonTransparent);

				// Doesn't actually do anything for this pass, except tells the rendergraph system that it gets written to
				pass.WriteTexture(dummy);
				pass.ReadTexture("", dummy);

				pass.ReadTexture("ParticleShadowWrite", target);
				pass.AddRenderPassData<CameraDepthData>();
				pass.AddRenderPassData<ViewData>(); 

				pass.ReadBuffer("PerCascadeData", perCascadeData);
				var voxelSize = (request.Far - request.Near) / settings.DirectionalDepth;

				pass.SetRenderFunction((dummy, target, index, settings.DirectionalResolution, settings.DirectionalDepth, zClip, voxelSize), static (command, pass, data) =>
				{
					pass.SetFloat("_ZClip", data.zClip ? 1.0f : 0.0f);
					pass.SetFloat("ParticleShadowDepth", data.DirectionalDepth);
					pass.SetFloat("ParticleShadowResolution", data.DirectionalResolution);
					pass.SetFloat("ParticleShadowIndex", data.index);
					pass.SetFloat("ParticleVoxelSize", data.voxelSize);
					command.ClearRandomWriteTargets();
					command.SetRandomWriteTarget(1, pass.GetRenderTexture(data.target));
				});
			}
		}

		var directionalShadowSizes = ArrayPool<float>.Get(directionalShadowCount);
		using (renderGraph.AddProfileScope($"Directional Shadows"))
		{
			// TODO: This needs to use the actual count or it will access out of bounds
			for (var i = 0; i < requestData.DirectionalShadowRequests.Count; i++)
			{
				using (renderGraph.AddProfileScope(directionalCascadeIds[i]))
				{
					var request = requestData.DirectionalShadowRequests[i];
					RenderShadowMap(request, directionalShadows, i, true, false, false);
					directionalShadowSizes[i] = (request.Far - request.Near) / settings.DirectionalDepth;
				}
			}

			ListPool<ShadowRequest>.Release(requestData.DirectionalShadowRequests);
		}

		// Accumulate directional shadows
		var result = renderGraph.GetTexture(settings.DirectionalResolution * directionalShadowCount, settings.DirectionalResolution, GraphicsFormat.R8_UNorm, settings.DirectionalDepth, TextureDimension.Tex3D, isRandomWrite: true, isExactSize: true);
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Particle Shadow Directional Accumulate"))
		{
			pass.Initialize(accumulateShader, 0, settings.DirectionalResolution * directionalShadowCount, settings.DirectionalResolution, 1);
			var shadowSteps = renderGraph.GetBuffer(directionalShadowCount);

			pass.ReadBuffer("ParticleShadowStep", shadowSteps);
			pass.WriteBuffer("ParticleShadowStep", shadowSteps);
			pass.WriteTexture("ParticleShadowWrite", result);
			pass.ReadTexture("ParticleExtinction", directionalShadows);

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetInt("ParticleShadowDepthInt", settings.DirectionalDepth);
				pass.SetInt("ParticleShadowResolutionInt", settings.DirectionalResolution);
				command.ClearRandomWriteTargets();
				command.SetBufferData(pass.GetBuffer(shadowSteps), directionalShadowSizes);
				ArrayPool<float>.Release(directionalShadowSizes);
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
