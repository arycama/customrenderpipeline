using UnityEngine;
using UnityEngine.Rendering;

public class GpuDrivenRenderingRender : CameraRenderFeature
{
	private GpuDrivenRenderer gpuDrivenRenderer;

	public GpuDrivenRenderingRender(GpuDrivenRenderer gpuDrivenRenderer, RenderGraph renderGraph) : base(renderGraph)
	{
		this.gpuDrivenRenderer = gpuDrivenRenderer;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game && camera.cameraType != CameraType.Reflection)
			return;

		if (!renderGraph.ResourceMap.TryGetResource<GpuDrivenRenderingData>(renderGraph.FrameIndex, out var instanceData))
			return;

		if (!instanceData.rendererDrawCallData.TryGetValue("MotionVectors", out var drawList))
			return;

		using var scope = renderGraph.AddProfileScope("Gpu Driven Rendering");

		var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().cullingPlanes;
		var renderingData = gpuDrivenRenderer.Setup(camera.ScaledViewSize(), false, cullingPlanes, instanceData);

		using var renderScope = renderGraph.AddProfileScope("Render");

		for (var i = 0; i < drawList.Count; i++)
		{
			var draw = drawList[i];
			using (var pass = renderGraph.AddDrawInstancedIndirectRenderPass("Gpu Driven Rendering", (draw.lodOffset, draw.objectToWorld)))
			{
				pass.Initialize(draw.mesh, draw.submeshIndex, draw.material, instanceData.drawCallArgs, draw.passIndex, 0.0f, 0.0f, true, draw.indirectArgsOffset);
				pass.AddKeyword("INDIRECT_RENDERING");
				pass.UseProfiler = false;

				pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
				pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
				pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
				pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());
				pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
				pass.WriteTexture(renderGraph.GetRTHandle<CameraVelocity>());
				pass.AddRenderPassData<AutoExposureData>();
				pass.AddRenderPassData<TemporalAAData>();
				pass.AddRenderPassData<AtmospherePropertiesAndTables>();
				pass.AddRenderPassData<ViewData>();
				pass.AddRenderPassData<FrameData>();

				pass.ReadBuffer("_VisibleRendererInstanceIndices", renderingData.visibilityPredicates);
				pass.ReadBuffer("_ObjectToWorld", renderingData.objectToWorld);
				pass.ReadBuffer("_InstancePositions", instanceData.positions);
				pass.ReadBuffer("_InstanceLodFades", instanceData.lodFades);
				pass.ReadBuffer("InstanceIdOffsets", renderingData.instanceIdOffsetsBuffer);

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					pass.SetInt("InstanceIdOffsetsIndex", data.lodOffset);
					pass.SetMatrix("LocalToWorld", (Matrix4x4)data.objectToWorld);
				});
			}
		}
	}
}