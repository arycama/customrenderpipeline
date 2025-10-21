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

		var albedoMetallic = renderGraph.GetRTHandle<GBufferAlbedoMetallic>();
		var normalRoughness = renderGraph.GetRTHandle<GBufferNormalRoughness>();
		var bentNormalOcclusion = renderGraph.GetRTHandle<GBufferBentNormalOcclusion>();

		var albedoMetallicCopy = renderGraph.GetTexture(albedoMetallic);
		var normalRoughnessCopy = renderGraph.GetTexture(normalRoughness);
		var bentNormalOcclusionCopy = renderGraph.GetTexture(bentNormalOcclusion);

		// Copy the existing gbuffer textures to new ones
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Copy"))
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
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Combine"))
		{
			pass.Initialize(material, 1);

			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(albedoMetallic);
			pass.WriteTexture(normalRoughness);
			pass.WriteTexture(bentNormalOcclusion);

			pass.ReadTexture("AlbedoMetallicCopy", albedoMetallicCopy);
			pass.ReadTexture("NormalRoughnessCopy", normalRoughnessCopy);
			pass.ReadTexture("BentNormalOcclusionCopy", bentNormalOcclusionCopy);

			pass.ReadRtHandle<CameraTarget>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<DecalAlbedo>();
			pass.ReadRtHandle<DecalNormal>();
			pass.AddRenderPassData<RainTextureResult>();

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetVector("AlbedoMetallicCopyScaleLimit", pass.GetScaleLimit2D(albedoMetallicCopy));
				pass.SetVector("NormalRoughnessCopyScaleLimit", pass.GetScaleLimit2D(normalRoughnessCopy));
				pass.SetVector("BentNormalOcclusionCopyScaleLimit", pass.GetScaleLimit2D(bentNormalOcclusionCopy));
			});
		}
	}
}
