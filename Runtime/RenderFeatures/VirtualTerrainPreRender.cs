using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class VirtualTerrainPreRender : CameraRenderFeature
{
	private readonly TerrainSettings settings;
	private readonly ResourceHandle<GraphicsBuffer> feedbackBuffer;
	private readonly Texture2DArray albedoSmoothnessTexture, normalTexture, heightTexture;
	private readonly ResourceHandle<RenderTexture> indirectionTexture;
	private readonly ComputeShader virtualTextureUpdateShader, dxtCompressCS, virtualTextureBuild, reductionComputeShader;

	private bool needsClear;
	private Terrain previousTerrain;

	private int IndirectionTextureResolution => settings.VirtualResolution / settings.TileResolution;

	public VirtualTerrainPreRender(RenderGraph renderGraph, TerrainSettings settings) : base(renderGraph)
	{
		this.settings = settings;
		var indirectionTextureResolution = settings.VirtualResolution / settings.TileSize;
		var requestSize = indirectionTextureResolution * indirectionTextureResolution * 4 / 3;
		feedbackBuffer = renderGraph.GetBuffer(requestSize, isPersistent: true);

		indirectionTexture = renderGraph.GetTexture(indirectionTextureResolution, indirectionTextureResolution, GraphicsFormat.R16_UInt, hasMips: true, isRandomWrite: true, isPersistent: true);

		albedoSmoothnessTexture = new Texture2DArray(settings.TileResolution, settings.TileResolution, settings.VirtualTileCount, TextureFormat.DXT5, 2, false)
		{
			hideFlags = HideFlags.HideAndDontSave,
			name = "Virtual AlbedoSmoothness Texture",
		};

		normalTexture = new Texture2DArray(settings.TileResolution, settings.TileResolution, settings.VirtualTileCount, TextureFormat.DXT5, 2, true)
		{
			hideFlags = HideFlags.HideAndDontSave,
			name = "Virtual Normal Texture",
		};

		heightTexture = new Texture2DArray(settings.TileResolution, settings.TileResolution, settings.VirtualTileCount, TextureFormat.BC4, 2, true)
		{
			hideFlags = HideFlags.HideAndDontSave,
			name = "Virtual Height Texture",
		};

		reductionComputeShader = Resources.Load<ComputeShader>("Terrain/VirtualTerrain");
		virtualTextureBuild = Resources.Load<ComputeShader>("Terrain/VirtualTextureBuild");
		virtualTextureUpdateShader = Resources.Load<ComputeShader>("Terrain/VirtualTextureUpdate");
		dxtCompressCS = Resources.Load<ComputeShader>("Terrain/DxtCompress");

		// Fill all buffesr with 0 (I think this should happen automatically, but
		using (var pass = renderGraph.AddGenericRenderPass("Virtual Texture Init"))
		{
			pass.WriteBuffer("", feedbackBuffer);
			pass.SetRenderFunction((command, pass) =>
			{
				command.SetBufferData(pass.GetBuffer(feedbackBuffer), new int[requestSize]);
			});
		}
	}

	protected override void Cleanup(bool disposing)
	{
		renderGraph.ReleasePersistentResource(feedbackBuffer);
		renderGraph.ReleasePersistentResource(indirectionTexture);

		Object.DestroyImmediate(albedoSmoothnessTexture);
		Object.DestroyImmediate(normalTexture);
		Object.DestroyImmediate(heightTexture);
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		// Ensure terrain system data is set
		if (!renderGraph.TryGetResource<TerrainSystemData>(out var terrainSystemData))
			return;

		// If terrain is different, clear the LRU cache
		if (terrainSystemData.terrain != previousTerrain || needsClear)
		{
			// TODO: Can we not just use a hardware clear?
			var indirectionMipCount = Texture2DExtensions.MipCount(IndirectionTextureResolution) - 1;
			for (var i = 0; i < indirectionMipCount; i++)
			{
				var mipSize = Texture2DExtensions.MipResolution(i, IndirectionTextureResolution);
				using (var pass = renderGraph.AddComputeRenderPass("Clear Texture"))
				{
					pass.Initialize(virtualTextureUpdateShader, 4, mipSize, mipSize);
					pass.WriteTexture("DestMip", indirectionTexture);
				}
			}

			needsClear = false;
		}

		previousTerrain = terrainSystemData.terrain;

		renderGraph.SetResource<VirtualTextureData>(new(albedoSmoothnessTexture, normalTexture, heightTexture, indirectionTexture, feedbackBuffer, settings.AnisoLevel, settings.VirtualResolution));
	}
}
