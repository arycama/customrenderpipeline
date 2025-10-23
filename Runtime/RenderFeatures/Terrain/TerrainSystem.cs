using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
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

	public Terrain Terrain { get; private set; }
	public TerrainData TerrainData => Terrain.terrainData;
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
		if (terrain != this.Terrain)
			return;

		InitializeHeightmap();
		InitializeIdMap(true, heightRegion);
	}

	private void TerrainCallbacks_textureChanged(Terrain terrain, string textureName, RectInt texelRegion, bool synched)
	{
		if (terrain == this.Terrain && textureName == TerrainData.AlphamapTextureName)
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
		var resolution = TerrainData.heightmapResolution;
		minMaxHeight = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R16G16_UNorm, hasMips: true, isPersistent: true);
		heightmap = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R16_UNorm, isPersistent: true);
		normalmap = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R8G8_SNorm, autoGenerateMips: true, hasMips: true, isPersistent: true);
		indexBuffer = renderGraph.GetGridIndexBuffer(settings.PatchVertices, true, false);

		InitializeHeightmap();

		// Initialize terrain layers
		terrainLayers.Clear();
		terrainProceduralLayers.Clear();
		var layers1 = TerrainData.terrainLayers;

		if (layers1 != null)
		{
			for (var i = 0; i < layers1.Length; i++)
			{
				terrainLayers.Add(layers1[i], i);
			}
		}

		var idMapResolution = TerrainData.alphamapResolution;
		idMap = renderGraph.GetTexture(idMapResolution, idMapResolution, GraphicsFormat.R32_UInt, isPersistent: true);
		aoMap = renderGraph.GetTexture(TerrainData.heightmapResolution, TerrainData.heightmapResolution, GraphicsFormat.R8G8B8A8_SNorm, isPersistent: true);

		// Process any alphamap modifications
		var alphamapModifiers = Terrain.GetComponents<ITerrainAlphamapModifier>();

		// Need to do some setup bvefore the graph executes to calculate buffer sizes
		foreach (var component in alphamapModifiers)
			component.PreGenerate(terrainLayers, terrainProceduralLayers);

		var layerCount = terrainLayers.Count;
		if (alphamapModifiers.Length > 0 && layerCount > 0)
		{
			using (var pass = renderGraph.AddGenericRenderPass("Terrain Generate Alphamap Callback", (idMapResolution, layerCount, alphamapModifiers, terrainLayers, terrainProceduralLayers, idMap)))
			{
				pass.SetRenderFunction(static (command, pass, data) =>
				{
					var tempArrayId = Shader.PropertyToID("_TempTerrainId");
					command.GetTemporaryRTArray(tempArrayId, data.idMapResolution, data.idMapResolution, data.layerCount, 0, FilterMode.Bilinear, GraphicsFormat.R16_SFloat, 1, true);

					foreach (var component in data.alphamapModifiers)
					{
						component.Generate(command, data.terrainLayers, data.terrainProceduralLayers, pass.GetRenderTexture(data.idMap));
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

		using (var pass = renderGraph.AddGenericRenderPass("Terrain Layer Data Init", (terrainLayers, diffuseArray, normalMapArray, maskMapArray)))
		{
			pass.SetRenderFunction(static (command, pass, data) =>
			{
				// Add to texture array
				foreach (var layer in data.terrainLayers)
				{
					command.CopyTexture(layer.Key.diffuseTexture, 0, data.diffuseArray, layer.Value);
					command.CopyTexture(layer.Key.normalMapTexture, 0, data.normalMapArray, layer.Value);
					command.CopyTexture(layer.Key.maskMapTexture, 0, data.maskMapArray, layer.Value);
				}
			});
		}

		FillLayerData();

		InitializeIdMap(false);

		renderGraph.SetResource(new TerrainSystemData(minMaxHeight, Terrain, TerrainData, indexBuffer), true);
	}

	private void InitializeHeightmap()
	{
		var resolution = TerrainData.heightmapResolution;
		var computeShader = Resources.Load<ComputeShader>("Terrain/InitTerrain");

		// Initialize the height map
		using (var pass = renderGraph.AddComputeRenderPass("Terrain Heightmap Init", TerrainData.heightmapTexture))
		{
			pass.Initialize(computeShader, 0, resolution, resolution);
			pass.WriteTexture("HeightmapResult", heightmap, 0);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				// Can't use pass.readTexture here since this comes from unity
				pass.SetTexture("HeightmapInput", data);
			});
		}

		// Process any heightmap modifications
		var heightmapModifiers = Terrain.GetComponents<ITerrainHeightmapModifier>();
		if (heightmapModifiers.Length > 0)
		{
			using (var pass = renderGraph.AddGenericRenderPass("Terrain Generate Heightmap Callback", (heightmapModifiers, heightmap, TerrainData.heightmapTexture)))
			{
				pass.SetRenderFunction(static (command, pass, data) =>
				{
					foreach (var component in data.heightmapModifiers)
					{
						component.Generate(command, data.heightmap, data.heightmapTexture);
					}
				});
			}
		}

		// Initialize normal map
		using (var pass = renderGraph.AddComputeRenderPass("Terrain Normalmap Init", (TerrainData.heightmapTexture, TerrainData.heightmapScale, TerrainData.size.y, TerrainData.heightmapResolution)))
		{
			pass.Initialize(computeShader, 1, resolution, resolution);
			pass.WriteTexture("InitNormalMapOutput", normalmap, 0);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				// Can't use pass.readTexture here since this comes from unity
				pass.SetTexture("InitNormalMapInput", data.heightmapTexture);

				var scale = data.heightmapScale;
				var height = data.y;
				pass.SetInt("MaxWidth", data.heightmapResolution - 1);
				pass.SetInt("MaxHeight", data.heightmapResolution - 1);
				pass.SetVector("Scale", new Float2(height / (8f * scale.x), height / (8f * scale.z)));
			});
		}

		// Hrm, just used for node graphs..
		var terrainRenderers = Terrain.GetComponents<ITerrainRenderer>();
		if (terrainRenderers.Length > 0)
		{
			using (var pass = renderGraph.AddGenericRenderPass("Terrain Generate Heightmap Callback", (terrainRenderers, heightmap, normalmap)))
			{
				pass.SetRenderFunction(static (command, pass, data) =>
				{
					foreach (var component in data.terrainRenderers)
					{
						component.Heightmap = pass.GetRenderTexture(data.heightmap);
						component.NormalMap = pass.GetRenderTexture(data.normalmap);
					}
				});
			}
		}

		// Generate min/max for terrain.
		// Todo: Could generate this as part of heightmap pass using LDS
		using (var pass = renderGraph.AddComputeRenderPass("Terrain Min Max Height Init"))
		{
			pass.Initialize(computeShader, 2, resolution, resolution);
			pass.WriteTexture("DepthCopyResult", minMaxHeight, 0);
			pass.ReadTexture("DepthCopyInput", heightmap);
		}

		var mipCount = Texture2DExtensions.MipCount(resolution, resolution);
		for (var i = 1; i < mipCount; i++)
		{
			var index = i;
			using (var pass = renderGraph.AddComputeRenderPass("Terrain Min Max Height", (resolution, index)))
			{
				// For some resolutions, we can end up with a 1x2, followed by a 1x1.
				var xSize = Mathf.Max(1, resolution >> index);
				var ySize = Mathf.Max(1, resolution >> index);

				pass.Initialize(computeShader, 3, xSize, ySize);
				pass.WriteTexture("GenerateMinMaxHeightsResult", minMaxHeight, index);
				pass.WriteTexture("GenerateMinMaxHeightsInput", minMaxHeight, index - 1);

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					var prevWidth = Mathf.Max(1, data.resolution >> (data.index - 1));
					var prevHeight = Mathf.Max(1, data.resolution >> (data.index - 1));

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

		using (var pass = renderGraph.AddGenericRenderPass("Terrain Layer Data Init", (terrainLayerData, terrainLayers)))
		{
			pass.WriteBuffer("", terrainLayerData);
			pass.SetRenderFunction(static (command, pass, data) =>
			{
				using var layerData = pass.GetBuffer(data.terrainLayerData).DirectWrite<TerrainLayerData>();
				foreach (var layer in data.terrainLayers)
				{
					var index = layer.Value;

					// Convert opacity at distance to density
					var extinction = Rcp(Max(1e-3f, Square(1.0f - layer.Key.smoothness))) / layer.Key.metallic;

					layerData.SetData(index, new TerrainLayerData(Rcp(layer.Key.tileSize.x), extinction, layer.Key.normalScale, layer.Key.metallic));
				}
			});
		}
	}

	private void InitializeIdMap(bool isUpdate, RectInt? texelRegion = null)
	{
		if (terrainLayers.Count == 0)
			return;

		var idMapResolution = TerrainData.alphamapResolution;
		using (var pass = renderGraph.AddFullscreenRenderPass("Terrain Layer Data Init", (TerrainData.alphamapLayers, (Float3)Terrain.terrainData.size, terrainLayers.Count, TerrainData.alphamapLayers, TerrainData.alphamapTextureCount, TerrainData.alphamapTextures)))
		{
			//var indicesBuffer = renderGraph.GetBuffer(terrainLayers.Count);
			pass.Initialize(generateIdMapMaterial);
			pass.WriteTexture(idMap);
			pass.ReadTexture("TerrainNormalMap", normalmap);
			//pass.WriteBuffer("ProceduralIndices", indicesBuffer);
			//pass.ReadBuffer("ProceduralIndices", indicesBuffer);
			pass.ReadBuffer("TerrainLayerData", terrainLayerData);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetInt("LayerCount", data.Item1);
				pass.SetVector("TerrainSize", data.Item2);
				pass.SetInt("TotalLayers", data.Count);
				pass.SetInt("TextureCount", data.Item4);
				pass.SetVector("PositionOffset", Float2.Zero);

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
					var texture = i < data.alphamapTextureCount ? data.alphamapTextures[i] : Texture2D.blackTexture;
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
				//pass.SetTexture("ExtraLayers", tempArrayId);
			});
		}

		if (!isUpdate)
		{
			using (var pass = renderGraph.AddFullscreenRenderPass("Terrain AO Map", (TerrainData, settings)))
			{
				pass.Initialize(terrainAmbientOcclusionMaterial);
				pass.WriteTexture(aoMap);
				pass.ReadTexture("TerrainNormalMap", normalmap);

				pass.SetRenderFunction((System.Action<CommandBuffer, RenderPass, (TerrainData terrainData, TerrainSettings settings)>)(static (command, pass, data) =>
				{
					pass.SetTexture("TerrainHeightmap", data.terrainData.heightmapTexture);
					pass.SetFloat("DirectionCount", data.settings.AmbientOcclusionDirections);
					pass.SetFloat("SampleCount", data.settings.AmbientOcclusionSamples);
					pass.SetFloat("Radius", data.settings.AmbientOcclusionRadius / data.terrainData.size.x);
					pass.SetFloat("Resolution", data.terrainData.heightmapResolution);

					var kmaxHeight = 32766.0f / 65535.0f;
					pass.SetFloat("TerrainHeightmapScaleY", data.terrainData.heightmapScale.y / kmaxHeight);
					pass.SetVector("TerrainHeightmapScale", (Float3)data.terrainData.heightmapScale);
					pass.SetVector("TerrainSize", (Float3)data.terrainData.size);

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
				}));
			}
		}
	}

	public override void Render(ScriptableRenderContext context)
	{
		// TODO: Logic here seems a bit off
		if (Terrain != Terrain.activeTerrain)
		{
			if (Terrain != null)
				CleanupResources();

			Terrain = Terrain.activeTerrain;
			if (Terrain == null)
				return;

			InitializeTerrain();
		}
		else
		{
			if (Terrain == null)
				return;

			using var scope = ListPool<ITerrainAlphamapModifier>.Get(out var alphamapModifiers);
			Terrain.GetComponents(alphamapModifiers);
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
