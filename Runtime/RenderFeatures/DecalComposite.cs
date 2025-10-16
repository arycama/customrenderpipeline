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

		var albedoMetallic = renderGraph.GetResource<AlbedoMetallicData>();
		var normalRoughness = renderGraph.GetResource<NormalRoughnessData>();

		var albedoMetallicCopy = renderGraph.GetTexture(albedoMetallic);
		var normalRoughnessCopy = renderGraph.GetTexture(normalRoughness);

		// Copy the existing gbuffer textures to new ones
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Copy"))
		{
			pass.Initialize(material, 0);

			pass.WriteDepth(renderGraph.GetResource<CameraDepthData>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(albedoMetallicCopy);
			pass.WriteTexture(normalRoughnessCopy);

			pass.AddRenderPassData<AlbedoMetallicData>();
			pass.AddRenderPassData<NormalRoughnessData>();
		}

		// Now composite the decal buffers
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Combine"))
		{
			pass.Initialize(material, 1);

			pass.WriteDepth(renderGraph.GetResource<CameraDepthData>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(albedoMetallic);
			pass.WriteTexture(normalRoughness);

			pass.ReadTexture("AlbedoMetallicCopy", albedoMetallicCopy);
			pass.ReadTexture("NormalRoughnessCopy", normalRoughnessCopy);

			pass.AddRenderPassData<DecalAlbedoData>();
			pass.AddRenderPassData<DecalNormalData>();
		}
	}
}
