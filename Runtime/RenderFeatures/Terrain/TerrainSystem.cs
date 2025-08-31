using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static Math;

public class TerrainSystem : FrameRenderFeature
{
	private readonly TerrainSettings settings;
	public ResourceHandle<GraphicsBuffer> terrainLayerData, indexBuffer;
	public ResourceHandle<RenderTexture> minMaxHeight, heightmap, normalmap, idMap, aoMap;
	private readonly Material generateIdMapMaterial, terrainAmbientOcclusionMaterial;
	public readonly Dictionary<TerrainLayer, int> terrainLayers = new();
	private readonly Dictionary<TerrainLayer, int> terrainProceduralLayers = new();

	public Texture2DArray diffuseArray, normalMapArray, maskMapArray;

	public Terrain terrain { get; private set; }
	public TerrainData terrainData => terrain.terrainData;
	protected int VerticesPerTileEdge => settings.PatchVertices + 1;
	protected int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;

	public TerrainSystem(RenderGraph renderGraph, TerrainSettings settings) : base(renderGraph)
	{
		this.settings = settings;

		generateIdMapMaterial = new Material(Shader.Find("Hidden/Terrain Id Map")) { hideFlags = HideFlags.HideAndDontSave };
		terrainAmbientOcclusionMaterial = new Material(Shader.Find("Hidden/Terrain Ambient Occlusion")) { hideFlags = HideFlags.HideAndDontSave };

		TerrainCallbacks.textureChanged += TerrainCallbacks_textureChanged;
		TerrainCallbacks.heightmapChanged += TerrainCallbacks_heightmapChanged;
	}

	protected override void Cleanup(bool disposing)
	{
		TerrainCallbacks.textureChanged -= TerrainCallbacks_textureChanged;
		TerrainCallbacks.heightmapChanged -= TerrainCallbacks_heightmapChanged;

		if (diffuseArray != null)
			Object.DestroyImmediate(diffuseArray);

		if (normalMapArray != null)
			Object.DestroyImmediate(normalMapArray);

		if (maskMapArray != null)
			Object.DestroyImmediate(maskMapArray);
	}

	private void TerrainCallbacks_heightmapChanged(Terrain terrain, RectInt heightRegion, bool synched)
	{
		if (terrain != this.terrain)
			return;

		InitializeHeightmap();
		InitializeIdMap(true, heightRegion);
	}

	private void TerrainCallbacks_textureChanged(Terrain terrain, string textureName, RectInt texelRegion, bool synched)
	{
		if (terrain == this.terrain && textureName == TerrainData.AlphamapTextureName)
			InitializeIdMap(true, texelRegion);
	}

	private void CleanupResources()
	{
		renderGraph.ReleasePersistentResource(minMaxHeight);
		renderGraph.ReleasePersistentResource(heightmap);
		renderGraph.ReleasePersistentResource(normalmap);
		renderGraph.ReleasePersistentResource(idMap);
		renderGraph.ReleasePersistentResource(aoMap);
		Object.DestroyImmediate(diffuseArray);
		Object.DestroyImmediate(normalMapArray);
		Object.DestroyImmediate(maskMapArray);
		renderGraph.ReleasePersistentResource(terrainLayerData);
		renderGraph.ReleasePersistentResource(indexBuffer);
	}

	private void InitializeTerrain()
	{
		var resolution = terrainData.heightmapResolution;
		minMaxHeight = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R16G16_UNorm, hasMips: true, isPersistent: true);
		heightmap = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R16_UNorm, isPersistent: true);
		normalmap = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R8G8_SNorm, autoGenerateMips: true, hasMips: true, isPersistent: true);

		indexBuffer = renderGraph.GetBuffer(QuadListIndexCount, sizeof(ushort), GraphicsBuffer.Target.Index, GraphicsBuffer.UsageFlags.LockBufferForWrite, true);

		using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Terrain Set Index Data"))
		{
			pass.WriteBuffer("", indexBuffer);
			pass.SetRenderFunction((command, pass) =>
			{
				using var indices = pass.GetBuffer(indexBuffer).DirectWrite<ushort>();

				for (int y = 0, i = 0; y < settings.PatchVertices; y++)
				{
					var rowStart = y * VerticesPerTileEdge;

					for (var x = 0; x < settings.PatchVertices; x++, i += 4)
					{
						indices.SetData(i + 0, (ushort)(rowStart + x));
						indices.SetData(i + 1, (ushort)(rowStart + x + VerticesPerTileEdge));
						indices.SetData(i + 2, (ushort)(rowStart + x + VerticesPerTileEdge + 1));
						indices.SetData(i + 3, (ushort)(rowStart + x + 1));
					}
				}
			});
		}

		InitializeHeightmap();

		// Initialize terrain layers
		terrainLayers.Clear();
		terrainProceduralLayers.Clear();
		var layers1 = terrainData.terrainLayers;

		if (layers1 != null)
		{
			for (var i = 0; i < layers1.Length; i++)
			{
				terrainLayers.Add(layers1[i], i);
			}
		}

		var idMapResolution = terrainData.alphamapResolution;
		idMap = renderGraph.GetTexture(idMapResolution, idMapResolution, GraphicsFormat.R32_UInt, isPersistent: true);
		aoMap = renderGraph.GetTexture(terrainData.heightmapResolution, terrainData.heightmapResolution, GraphicsFormat.R8G8B8A8_SNorm, isPersistent: true);

		// Process any alphamap modifications
		var alphamapModifiers = terrain.GetComponents<ITerrainAlphamapModifier>();

		// Need to do some setup bvefore the graph executes to calculate buffer sizes
		foreach (var component in alphamapModifiers)
			component.PreGenerate(terrainLayers, terrainProceduralLayers);

		var layerCount = terrainLayers.Count;
		if (alphamapModifiers.Length > 0 && layerCount > 0)
		{
			using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Terrain Generate Alphamap Callback"))
			{
				pass.SetRenderFunction((command, pass) =>
				{
					var tempArrayId = Shader.PropertyToID("_TempTerrainId");
					command.GetTemporaryRTArray(tempArrayId, idMapResolution, idMapResolution, layerCount, 0, FilterMode.Bilinear, GraphicsFormat.R16_SFloat, 1, true);

					foreach (var component in alphamapModifiers)
					{
						component.Generate(command, terrainLayers, terrainProceduralLayers, pass.GetRenderTexture(idMap));
					}
				});
			}
		}

		int diffuseWidth = 1, diffuseHeight = 1, normalWidth = 1, normalHeight = 1, maskWidth = 1, maskHeight = 1;
		GraphicsFormat diffuseFormat = GraphicsFormat.None, normalFormat = GraphicsFormat.None, maskFormat = GraphicsFormat.None;

		foreach (var layer in terrainLayers)
		{
			static void CheckTexture(Texture2D texture, ref int width, ref int height, ref GraphicsFormat format)
			{
				if (texture == null)
					return;

				width = Max(width, texture.width);
				height = Max(height, texture.height);

				if (format == GraphicsFormat.None)
					format = texture.graphicsFormat;
				else if (texture.graphicsFormat != format)
					Debug.LogWarning($"Texture format {texture.graphicsFormat} does not match previous format of {format} and will not be rendered");
			}

			CheckTexture(layer.Key.diffuseTexture, ref diffuseWidth, ref diffuseHeight, ref diffuseFormat);
			CheckTexture(layer.Key.normalMapTexture, ref normalWidth, ref normalHeight, ref normalFormat);
			CheckTexture(layer.Key.maskMapTexture, ref maskWidth, ref maskHeight, ref maskFormat);
		}

		var arraySize = Mathf.Max(1, layerCount);

		// Initialize arrays, use the first texture, since everything must be the same resolution
		// Create texture arrays and copy textures. (Initialize with defaults if empty to avoid errors
		var flags = TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate;
		diffuseArray = new(diffuseWidth, diffuseHeight, arraySize, diffuseFormat == GraphicsFormat.None ? GraphicsFormat.R8_UNorm : diffuseFormat, flags) { name = "Terrain Diffuse" };
		diffuseArray.Apply(false, true);
		normalMapArray = new(normalWidth, normalHeight, arraySize, normalFormat == GraphicsFormat.None ? GraphicsFormat.R8_UNorm : normalFormat, flags) { name = "Terrain Normal" };
		normalMapArray.Apply(false, true);
		maskMapArray = new(maskWidth, maskHeight, arraySize, maskFormat == GraphicsFormat.None ? GraphicsFormat.R8_UNorm : maskFormat, flags) { name = "Terrain Mask" };
		maskMapArray.Apply(false, true);

		terrainLayerData = renderGraph.GetBuffer(arraySize, UnsafeUtility.SizeOf<TerrainLayerData>(), GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, true);

		using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Terrain Layer Data Init"))
		{
			pass.SetRenderFunction((command, pass) =>
			{
				// Add to texture array
				foreach (var layer in terrainLayers)
				{
					command.CopyTexture(layer.Key.diffuseTexture, 0, diffuseArray, layer.Value);
					command.CopyTexture(layer.Key.normalMapTexture, 0, normalMapArray, layer.Value);
					command.CopyTexture(layer.Key.maskMapTexture, 0, maskMapArray, layer.Value);
				}
			});
		}

		FillLayerData();

		InitializeIdMap(false);

		renderGraph.SetResource(new TerrainSystemData(minMaxHeight, terrain, terrainData, indexBuffer), true);
	}

	private void InitializeHeightmap()
	{
		var resolution = terrainData.heightmapResolution;
		var computeShader = Resources.Load<ComputeShader>("Terrain/InitTerrain");

		// Initialize the height map
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Terrain Heightmap Init"))
		{
			pass.Initialize(computeShader, 0, resolution, resolution);
			pass.WriteTexture("HeightmapResult", heightmap, 0);

			pass.SetRenderFunction((command, pass) =>
			{
				// Can't use pass.readTexture here since this comes from unity
				pass.SetTexture("HeightmapInput", terrainData.heightmapTexture);
			});
		}

		// Process any heightmap modifications
		var heightmapModifiers = terrain.GetComponents<ITerrainHeightmapModifier>();
		if (heightmapModifiers.Length > 0)
		{
			using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Terrain Generate Heightmap Callback"))
			{
				pass.SetRenderFunction((command, pass) =>
				{
					foreach (var component in heightmapModifiers)
					{
						component.Generate(command, heightmap, terrainData.heightmapTexture);
					}
				});
			}
		}

		// Initialize normal map
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Terrain Normalmap Init"))
		{
			pass.Initialize(computeShader, 1, resolution, resolution);
			pass.WriteTexture("InitNormalMapOutput", normalmap, 0);

			pass.SetRenderFunction((command, pass) =>
			{
				// Can't use pass.readTexture here since this comes from unity
				pass.SetTexture("InitNormalMapInput", terrainData.heightmapTexture);

				var scale = terrainData.heightmapScale;
				var height = terrainData.size.y;
				pass.SetInt("MaxWidth", terrainData.heightmapResolution - 1);
				pass.SetInt("MaxHeight", terrainData.heightmapResolution - 1);
				pass.SetVector("Scale", new Vector2(height / (8f * scale.x), height / (8f * scale.z)));
			});
		}

		// Hrm, just used for node graphs..
		var terrainRenderers = terrain.GetComponents<ITerrainRenderer>();
		if (terrainRenderers.Length > 0)
		{
			using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Terrain Generate Heightmap Callback"))
			{
				pass.SetRenderFunction((command, pass) =>
				{
					foreach (var component in terrainRenderers)
					{
						component.Heightmap = pass.GetRenderTexture(heightmap);
						component.NormalMap = pass.GetRenderTexture(normalmap);
					}
				});
			}
		}

		// Generate min/max for terrain.
		// Todo: Could generate this as part of heightmap pass using LDS
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Terrain Min Max Height Init"))
		{
			pass.Initialize(computeShader, 2, resolution, resolution);
			pass.WriteTexture("DepthCopyResult", minMaxHeight, 0);
			pass.ReadTexture("DepthCopyInput", heightmap);
		}

		var mipCount = Texture2DExtensions.MipCount(resolution, resolution);
		for (var i = 1; i < mipCount; i++)
		{
			var index = i;
			using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Terrain Min Max Height"))
			{
				// For some resolutions, we can end up with a 1x2, followed by a 1x1.
				var xSize = Mathf.Max(1, resolution >> index);
				var ySize = Mathf.Max(1, resolution >> index);

				pass.Initialize(computeShader, 3, xSize, ySize);
				pass.WriteTexture("GenerateMinMaxHeightsResult", minMaxHeight, index);
				pass.WriteTexture("GenerateMinMaxHeightsInput", minMaxHeight, index - 1);

				pass.SetRenderFunction((command, pass) =>
				{
					var prevWidth = Mathf.Max(1, resolution >> (index - 1));
					var prevHeight = Mathf.Max(1, resolution >> (index - 1));

					pass.SetInt("_Width", prevWidth);
					pass.SetInt("_Height", prevHeight);
				});
			}
		}
	}

	private void FillLayerData()
	{
		var count = terrainLayers.Count;
		if (count == 0)
			return;

		using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Terrain Layer Data Init"))
		{
			pass.WriteBuffer("", terrainLayerData);
			pass.SetRenderFunction((command, pass) =>
			{
				using var layerData = pass.GetBuffer(terrainLayerData).DirectWrite<TerrainLayerData>();
				foreach (var layer in terrainLayers)
				{
					var index = layer.Value;
					layerData.SetData(index, new TerrainLayerData(Rcp(layer.Key.tileSize.x), Mathf.Max(1e-3f, layer.Key.smoothness), layer.Key.normalScale, 1.0f - layer.Key.metallic));
				}
			});
		}
	}

	private void InitializeIdMap(bool isUpdate, RectInt? texelRegion = null)
	{
		if (terrainLayers.Count == 0)
			return;

		var idMapResolution = terrainData.alphamapResolution;
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Terrain Layer Data Init"))
		{
			//var indicesBuffer = renderGraph.GetBuffer(terrainLayers.Count);
			pass.Initialize(generateIdMapMaterial);
			pass.WriteTexture(idMap);
			pass.ReadTexture("TerrainNormalMap", normalmap);
			//pass.WriteBuffer("ProceduralIndices", indicesBuffer);
			//pass.ReadBuffer("ProceduralIndices", indicesBuffer);
			pass.ReadBuffer("TerrainLayerData", terrainLayerData);

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetInt("LayerCount", terrainData.alphamapLayers);
				pass.SetVector("TerrainSize", terrain.terrainData.size);
				pass.SetInt("TotalLayers", terrainLayers.Count);
				pass.SetInt("TextureCount", terrainData.alphamapLayers);
				pass.SetVector("PositionOffset", Vector2.zero);

				//if (texelRegion.HasValue)
				//{
				//	var region = texelRegion.Value;
				//	command.SetViewport(new Rect(region.position, region.size));
				//	pass.SetVector("PositionOffset", (Vector2)region.position);
				//	pass.SetVector("UvScaleOffset", new Vector4(region.size.x, region.size.y, region.position.x, region.position.y) / terrainData.alphamapResolution);
				//}
				//else
				{
					pass.SetVector("UvScaleOffset", new Vector4(1, 1, 0, 0));
				}

				// Shader supports up to 8 layers. Can easily be increased by modifying shader though
				for (var i = 0; i < 8; i++)
				{
					var texture = i < terrainData.alphamapTextureCount ? terrainData.alphamapTextures[i] : Texture2D.blackTexture;
					pass.SetTexture($"Input{i}", texture);
				}

				// Need to build buffer of layer to array index
				//var layers = new NativeArray<int>(terrainLayers.Count, Allocator.Temp);
				//foreach (var layer in terrainLayers)
				//{
				//	if (terrainProceduralLayers.TryGetValue(layer.Key, out var proceduralIndex))
				//	{
				//		// Use +1 so we can use 0 to indicate no data
				//		layers[layer.Value] = proceduralIndex + 1;
				//	}
				//}

				//command.SetBufferData(pass.GetBuffer(indicesBuffer), layers);
				//var tempArrayId = Shader.PropertyToID("TempTerrainId");
				//command.SetGlobalTexture("ExtraLayers", tempArrayId);
			});
		}

		if (!isUpdate)
		{
			using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Terrain AO Map"))
			{
				pass.Initialize(terrainAmbientOcclusionMaterial);
				pass.WriteTexture(aoMap);
				pass.ReadTexture("TerrainNormalMap", normalmap);

				pass.SetRenderFunction((command, pass) =>
				{
					pass.SetTexture("TerrainHeightmap", terrainData.heightmapTexture);
					pass.SetFloat("DirectionCount", settings.AmbientOcclusionDirections);
					pass.SetFloat("SampleCount", settings.AmbientOcclusionSamples);
					pass.SetFloat("Radius", settings.AmbientOcclusionRadius / terrainData.size.x);
					pass.SetFloat("Resolution", terrainData.heightmapResolution);

					var kmaxHeight = 32766.0f / 65535.0f;
					pass.SetFloat("TerrainHeightmapScaleY", terrainData.heightmapScale.y / kmaxHeight);
					pass.SetVector("TerrainHeightmapScale", terrainData.heightmapScale);
					pass.SetVector("TerrainSize", terrainData.size);

					//if (texelRegion.HasValue)
					//{
					//	var region = texelRegion.Value;
					//	command.SetViewport(new Rect(region.position, region.size));
					//	properties.SetVector("UvScaleOffset", new Vector4(region.size.x, region.size.y, region.position.x, region.position.y) / terrainData.heightmapResolution);
					//}
					//else
					{
						pass.SetVector("UvScaleOffset", new Vector4(1, 1, 0, 0));
					}
				});
			}
		}
	}

	public override void Render(ScriptableRenderContext context)
	{
		// TODO: Logic here seems a bit off
		if (terrain != Terrain.activeTerrain)
		{
			if (terrain != null)
				CleanupResources();

			terrain = Terrain.activeTerrain;
			if (terrain == null)
				return;

			InitializeTerrain();
		}
		else
		{
			if (terrain == null)
				return;

			var alphamapModifiers = terrain.GetComponents<ITerrainAlphamapModifier>();
			var needsUpdate = false;
			foreach (var alphamapModifier in alphamapModifiers)
			{
				if (!alphamapModifier.NeedsUpdate)
					continue;

				needsUpdate = true;
				break;
			}

			if (needsUpdate)
			{
				CleanupResources();
				InitializeTerrain();
			}
			else
			{
				// Set this every frame incase of changes..
				// TODO: Only do when data changed?
				// Only do this if terrain wasn't initialized, 
				FillLayerData();
			}
		}
	}
}

public class TerrainViewData : CameraRenderFeature
{
	private readonly TerrainSystem terrainSystem;

	public TerrainViewData(RenderGraph renderGraph, TerrainSystem terrainSystem) : base(renderGraph)
	{
		this.terrainSystem = terrainSystem;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		if (terrainSystem.terrain == null)
			return;

		var position = terrainSystem.terrain.GetPosition() - camera.transform.position;
		var size = terrainSystem.terrainData.size;
		var terrainScaleOffset = new Vector4(1f / size.x, 1f / size.z, -position.x / size.x, -position.z / size.z);
		var terrainRemapHalfTexel = GraphicsUtilities.HalfTexelRemap(position.XZ(), size.XZ(), Vector2.one * terrainSystem.terrainData.heightmapResolution);
		var terrainHeightOffset = position.y;
		renderGraph.SetResource(new TerrainRenderData(terrainSystem.diffuseArray, terrainSystem.normalMapArray, terrainSystem.maskMapArray, terrainSystem.heightmap, terrainSystem.normalmap, terrainSystem.idMap, terrainSystem.terrainData.holesTexture, terrainRemapHalfTexel, terrainScaleOffset, size, size.y, terrainHeightOffset, terrainSystem.terrainData.alphamapResolution, terrainSystem.terrainLayerData, terrainSystem.aoMap));

		// This sets raytracing data on the terrain's material property block
		using (var pass = renderGraph.AddRenderPass<SetPropertyBlockPass>("Terrain Data Property Block Update"))
		{
			var propertyBlock = pass.propertyBlock;
			terrainSystem.terrain.GetSplatMaterialPropertyBlock(propertyBlock);
			pass.AddRenderPassData<TerrainRenderData>();

			pass.SetRenderFunction((command, pass) =>
			{
				terrainSystem.terrain.SetSplatMaterialPropertyBlock(propertyBlock);
			});
		}
	}
}