using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static Math;

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

		using (var pass = renderGraph.AddRenderPass<FullscreenTerrainRenderPass>("Screen Space Terrain"))
		{
			pass.Initialize(material, terrain);
			var size = (Float2)terrain.terrainData.size.XZ();
			var scale = 1f / size;
			var offset = -scale * (terrain.GetPosition().XZ() - camera.transform.position.XZ());

			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());
			pass.AddRenderPassData<ViewData>();
			pass.ReadRtHandle<CameraDepth>();
			pass.AddRenderPassData<TerrainRenderData>();

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetVector("WorldToTerrain", new Float4(scale, offset));
			});
		}
	}
}
