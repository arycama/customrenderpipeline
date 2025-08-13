using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class PrecomputeDfg : FrameRenderFeature
{
	public override string ProfilerNameOverride => "Precompute Dfg";

	private Material precomputeDfgMaterial;
	private readonly ResourceHandle<RenderTexture> precomputeDfg, directionalAlbedo, averageAlbedo, directionalAlbedoMs, averageAlbedoMs, specularOcclusion;

	public PrecomputeDfg(RenderGraph renderGraph) : base(renderGraph)
	{
		precomputeDfgMaterial = new Material(Shader.Find("Hidden/PrecomputeDfg")) { hideFlags = HideFlags.HideAndDontSave };

		precomputeDfg = renderGraph.GetTexture(32, 32, GraphicsFormat.R16G16_UNorm, isPersistent: true, isExactSize: true);
		directionalAlbedo = renderGraph.GetTexture(32, 32, GraphicsFormat.R16_UNorm, isPersistent: true, isExactSize: true);
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Precompute Dfg"))
		{
			pass.Initialize(precomputeDfgMaterial, 0);
			pass.WriteTexture(precomputeDfg);
			pass.WriteTexture(directionalAlbedo);
		}

		averageAlbedo = renderGraph.GetTexture(32, 1, GraphicsFormat.R16_UNorm, isPersistent: true);
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Average Albedo"))
		{
			pass.Initialize(precomputeDfgMaterial, 1);
			pass.WriteTexture(averageAlbedo);
			pass.ReadTexture("DirectionalAlbedo", directionalAlbedo);
		}

		directionalAlbedoMs = renderGraph.GetTexture(16, 16, GraphicsFormat.R16_UNorm, 16, TextureDimension.Tex3D, isPersistent: true);
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Directional Albedo Ms"))
		{
			pass.Initialize(precomputeDfgMaterial, 2);
			pass.WriteTexture(directionalAlbedoMs);
			pass.ReadTexture("DirectionalAlbedo", directionalAlbedo);
		}

		averageAlbedoMs = renderGraph.GetTexture(16, 16, GraphicsFormat.R16_UNorm, isPersistent: true);
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Average Albedo Ms"))
		{
			pass.Initialize(precomputeDfgMaterial, 3);
			pass.WriteTexture(averageAlbedoMs);
			pass.ReadTexture("DirectionalAlbedoMs", directionalAlbedoMs);
		}

		specularOcclusion = renderGraph.GetTexture(32, 32, GraphicsFormat.R16_UNorm, 32 * 32, TextureDimension.Tex3D, isPersistent: true);
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Specular Occlusion"))
		{
			pass.Initialize(precomputeDfgMaterial, 4, 32);
			pass.WriteTexture(specularOcclusion);
		}

		renderGraph.SetResource(new DfgData(precomputeDfg, directionalAlbedo, averageAlbedo, directionalAlbedoMs, averageAlbedoMs, specularOcclusion), true);
	}

	protected override void Cleanup(bool disposing)
	{
		renderGraph.ReleasePersistentResource(precomputeDfg);
		renderGraph.ReleasePersistentResource(directionalAlbedo);
		renderGraph.ReleasePersistentResource(averageAlbedo);
		renderGraph.ReleasePersistentResource(directionalAlbedoMs);
		renderGraph.ReleasePersistentResource(averageAlbedoMs);
		renderGraph.ReleasePersistentResource(specularOcclusion);
	}

	public override void Render(ScriptableRenderContext context)
	{
	}
}
