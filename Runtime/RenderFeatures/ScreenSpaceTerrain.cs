using UnityEngine;
using UnityEngine.Rendering;

public class ScreenSpaceTerrain : CameraRenderFeature
{
	private readonly Material material;

	public ScreenSpaceTerrain(RenderGraph renderGraph) : base(renderGraph)
	{
		material = new Material(Shader.Find("Hidden/Screen Space Terrain")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var terrain = Terrain.activeTerrain;
		if (terrain == null)
			return;

		var size = terrain.terrainData.size.XZ();
		var scale = 1f / size;
		var offset = -scale * (terrain.GetPosition().XZ() - camera.transform.position.XZ());

		using (var pass = renderGraph.AddFullscreenTerrainRenderPass("Screen Space Terrain", new Float4(scale, offset)))
		{
			pass.Initialize(material, terrain);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());

			pass.ReadRtHandle<CameraDepth>();

			pass.AddRenderPassData<TerrainRenderData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<VirtualTextureData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetVector("WorldToTerrain", data);
			});
		}
	}
}
