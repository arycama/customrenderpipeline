using UnityEngine;
using UnityEngine.Rendering;

public class DecalComposite : CameraRenderFeature
{
	private readonly Material material;

	public DecalComposite(RenderGraph renderGraph) : base(renderGraph)
	{
		material = new Material(Shader.Find("Hidden/Decal Composite")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		using var scope = renderGraph.AddProfileScope("Decal Composite");

		var albedoMetallicCopy = renderGraph.GetTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
		var normalRoughnessCopy = renderGraph.GetTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
		var bentNormalOcclusionCopy = renderGraph.GetTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());

		// Copy the existing gbuffer textures to new ones
		using (var pass = renderGraph.AddFullscreenRenderPass("Copy"))
		{
			pass.Initialize(material, 0);

			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(albedoMetallicCopy);
			pass.WriteTexture(normalRoughnessCopy);
			pass.WriteTexture(bentNormalOcclusionCopy);

			pass.ReadRtHandle<GBufferAlbedoMetallic>();
			pass.ReadRtHandle<GBufferNormalRoughness>();
			pass.ReadRtHandle<GBufferBentNormalOcclusion>();
		}

		// Now composite the decal buffers
		using (var pass = renderGraph.AddFullscreenRenderPass("Combine", (albedoMetallicCopy, normalRoughnessCopy, bentNormalOcclusionCopy)))
		{
			pass.Initialize(material, 1);

			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());

			pass.ReadTexture("AlbedoMetallicCopy", albedoMetallicCopy);
			pass.ReadTexture("NormalRoughnessCopy", normalRoughnessCopy);
			pass.ReadTexture("BentNormalOcclusionCopy", bentNormalOcclusionCopy);

			pass.ReadRtHandle<CameraTarget>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<DecalAlbedo>();
			pass.ReadRtHandle<DecalNormal>();
			pass.AddRenderPassData<RainTextureResult>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetVector("AlbedoMetallicCopyScaleLimit", pass.GetScaleLimit2D(data.albedoMetallicCopy));
				pass.SetVector("NormalRoughnessCopyScaleLimit", pass.GetScaleLimit2D(data.normalRoughnessCopy));
				pass.SetVector("BentNormalOcclusionCopyScaleLimit", pass.GetScaleLimit2D(data.bentNormalOcclusionCopy));
			});
		}
	}
}
