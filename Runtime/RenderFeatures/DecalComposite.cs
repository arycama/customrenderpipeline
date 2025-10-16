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
		var newAlbedoMetallic = renderGraph.GetTexture(albedoMetallic);
		var normalRoughness = renderGraph.GetResource<NormalRoughnessData>();
		var newNormalRoughness = renderGraph.GetTexture(normalRoughness);

		// Copy the existing gbuffer textures to new ones
		using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Copy"))
		{
			pass.WriteTexture(newAlbedoMetallic);
			pass.WriteTexture(newNormalRoughness);

			pass.ReadTexture("", albedoMetallic);
			pass.ReadTexture("", normalRoughness);

			pass.SetRenderFunction((command, pass) =>
			{
				command.CopyTexture(pass.GetRenderTexture(albedoMetallic), pass.GetRenderTexture(newAlbedoMetallic));
				command.CopyTexture(pass.GetRenderTexture(normalRoughness), pass.GetRenderTexture(newNormalRoughness));
			});
		}

		// Now composite the decal buffers
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Combine"))
		{
			pass.Initialize(material);
			pass.WriteDepth(renderGraph.GetResource<CameraDepthData>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(newAlbedoMetallic);
			pass.WriteTexture(newNormalRoughness);
			pass.AddRenderPassData<AlbedoMetallicData>();
			pass.AddRenderPassData<NormalRoughnessData>();
			pass.AddRenderPassData<DecalAlbedoData>();
			pass.AddRenderPassData<DecalNormalData>();
		}

		// Write out the new textures as the current resources
		renderGraph.SetResource(new AlbedoMetallicData(newAlbedoMetallic));
		renderGraph.SetResource(new NormalRoughnessData(newNormalRoughness));
	}
}


