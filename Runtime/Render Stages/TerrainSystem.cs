using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Arycama.CustomRenderPipeline
{
    public partial class TerrainSystem : RenderFeature
    {
        private readonly TerrainSettings settings;
        private BufferHandle terrainLayerData, indexBuffer;
        private RTHandle minMaxHeight, heightmap, normalmap, idMap;
        private readonly Material generateIdMapMaterial;
        private readonly Dictionary<TerrainLayer, int> terrainLayers = new();
        private readonly Dictionary<TerrainLayer, int> terrainProceduralLayers = new();

        private Texture2DArray diffuseArray, normalMapArray, maskMapArray;

        public Terrain terrain { get; private set; }
        public TerrainData terrainData => terrain.terrainData;
        protected int VerticesPerTileEdge => settings.PatchVertices + 1;
        protected int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;

        public TerrainSystem(RenderGraph renderGraph, TerrainSettings settings) : base(renderGraph)
        {
            this.settings = settings;

            generateIdMapMaterial = new Material(Shader.Find("Hidden/Terrain Id Map")) { hideFlags = HideFlags.HideAndDontSave };

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
            if (terrain == this.terrain)
                InitializeHeightmap();
        }

        private void TerrainCallbacks_textureChanged(Terrain terrain, string textureName, RectInt texelRegion, bool synched)
        {
            if (terrain == this.terrain && textureName == TerrainData.AlphamapTextureName)
                InitializeIdMap();
        }

        private void CleanupResources()
        {
            renderGraph.ReleasePersistentResource(minMaxHeight);
            renderGraph.ReleasePersistentResource(heightmap);
            renderGraph.ReleasePersistentResource(normalmap);
            renderGraph.ReleasePersistentResource(idMap);
            Object.DestroyImmediate(diffuseArray);
            Object.DestroyImmediate(normalMapArray);
            Object.DestroyImmediate(maskMapArray);
            renderGraph.ReleasePersistentResource(terrainLayerData);
        }

        private void InitializeTerrain()
        {
            if (terrain != null)
                CleanupResources();

            terrain = Terrain.activeTerrain;
            if (terrain == null)
                return;

            var resolution = terrainData.heightmapResolution;
            minMaxHeight = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R16G16_UNorm, hasMips: true, isPersistent: true);
            heightmap = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R16_UNorm, isPersistent: true);
            normalmap = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R8G8_SNorm, autoGenerateMips: true, hasMips: true, isPersistent: true);

            indexBuffer = renderGraph.GetBuffer(QuadListIndexCount, sizeof(ushort), GraphicsBuffer.Target.Index, GraphicsBuffer.UsageFlags.LockBufferForWrite, true);

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Terrain Set Index Data"))
            {
                pass.WriteBuffer("", indexBuffer);
                pass.SetRenderFunction((command, pass) =>
                {
                    var pIndices = pass.GetBuffer(indexBuffer).LockBufferForWrite<ushort>(0, QuadListIndexCount);
                    for (int y = 0, i = 0; y < settings.PatchVertices; y++)
                    {
                        var rowStart = y * VerticesPerTileEdge;

                        for (var x = 0; x < settings.PatchVertices; x++, i += 4)
                        {
                            // Can do a checkerboard flip to avoid directioanl artifacts, but will mess with the tessellation code
                            //var flip = (x & 1) == (y & 1);

                            //if(flip)
                            //{
                            pIndices[i + 0] = (ushort)(rowStart + x);
                            pIndices[i + 1] = (ushort)(rowStart + x + VerticesPerTileEdge);
                            pIndices[i + 2] = (ushort)(rowStart + x + VerticesPerTileEdge + 1);
                            pIndices[i + 3] = (ushort)(rowStart + x + 1);
                            //}
                            //else
                            //{
                            //    pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge);
                            //    pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge + 1);
                            //    pIndices[index++] = (ushort)(rowStart + x + 1);
                            //    pIndices[index++] = (ushort)(rowStart + x);
                            //}
                        }
                    }

                    pass.GetBuffer(indexBuffer).UnlockBufferAfterWrite<ushort>(QuadListIndexCount);
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

            // Process any alphamap modifications
            var alphamapModifiers = terrain.GetComponents<ITerrainAlphamapModifier>();

            // Need to do some setup bvefore the graph executes to calculate buffer sizes
            foreach (var component in alphamapModifiers)
                component.PreGenerate(terrainLayers, terrainProceduralLayers);

            var layerCount = terrainLayers.Count;
            if (alphamapModifiers.Length > 0 && layerCount > 0)
            {
                using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Terrain Generate Alphamap Callback"))
                {
                    pass.SetRenderFunction((command, pass) =>
                    {
                        var tempArrayId = Shader.PropertyToID("_TempTerrainId");
                        command.GetTemporaryRTArray(tempArrayId, idMap.Width, idMap.Height, layerCount, 0, FilterMode.Bilinear, GraphicsFormat.R16_SFloat, 1, true);

                        foreach (var component in alphamapModifiers)
                        {
                            component.Generate(command, terrainLayers, terrainProceduralLayers, pass.GetRenderTexture(idMap));
                        }
                    });
                }
            }

            var arraySize = Mathf.Max(1, layerCount);

            // Initialize arrays, use the first texture, since everything must be the same resolution
            // TODO: Add checks/asserts/warnings for incorrect textures
            var flags = TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate;

            diffuseArray = new Texture2DArray(settings.DiffuseResolution, settings.DiffuseResolution, arraySize, settings.DiffuseFormat, flags);
            normalMapArray = new Texture2DArray(settings.NormalResolution, settings.NormalResolution, arraySize, settings.NormalFormat, flags);
            maskMapArray = new Texture2DArray(settings.MaskResolution, settings.MaskResolution, arraySize, settings.MaskFormat, flags);

            // Graph doesn't support persistent buffers yet
            // TODO: Add persistent buffer support 
            //var layerDataBuffer = renderGraph.GetBuffer(layers.Length, UnsafeUtility.SizeOf<TerrainLayerData>(), GraphicsBuffer.Target.Structured);
            terrainLayerData = renderGraph.GetBuffer(arraySize, UnsafeUtility.SizeOf<TerrainLayerData>(), GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, true);

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Terrain Layer Data Init"))
            {
                pass.SetRenderFunction((command, pass) =>
                {
                    // Add to texture array
                    foreach (var layer in terrainLayers)
                    {
                        //Debug.Log($"Copying {layer.Key.diffuseTexture.graphicsFormat}, ({layer.Key.diffuseTexture.width}x{layer.Key.diffuseTexture.height}");
                        command.CopyTexture(layer.Key.diffuseTexture, 0, diffuseArray, layer.Value);
                        //Debug.Log($"Copying {layer.Key.normalMapTexture.graphicsFormat}, ({layer.Key.normalMapTexture.width}x{layer.Key.normalMapTexture.height}");
                        command.CopyTexture(layer.Key.normalMapTexture, 0, normalMapArray, layer.Value);
                        //Debug.Log($"Copying {layer.Key.maskMapTexture.graphicsFormat}, ({layer.Key.maskMapTexture.width}x{layer.Key.maskMapTexture.height}");
                        command.CopyTexture(layer.Key.maskMapTexture, 0, maskMapArray, layer.Value);
                    }
                });
            }

            FillLayerData();

            InitializeIdMap();

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
                using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Terrain Generate Heightmap Callback"))
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
                using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Terrain Generate Heightmap Callback"))
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

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Terrain Layer Data Init"))
            {
                pass.WriteBuffer("", terrainLayerData);
                pass.SetRenderFunction((command, pass) =>
                {
                    var layerData = pass.GetBuffer(terrainLayerData).LockBufferForWrite<TerrainLayerData>(0, count);
                    foreach (var layer in terrainLayers)
                    {
                        var index = layer.Value;
                        layerData[index] = new TerrainLayerData(layer.Key.tileSize.x, Mathf.Max(1e-3f, layer.Key.smoothness), layer.Key.normalScale, 1.0f - layer.Key.metallic);
                    }

                    pass.GetBuffer(terrainLayerData).UnlockBufferAfterWrite<TerrainLayerData>(count);
                });
            }
        }

        private void InitializeIdMap()
        {
            if (terrainLayers.Count == 0)
                return;

            var idMapResolution = terrainData.alphamapResolution;
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Terrain Layer Data Init"))
            {
                var indicesBuffer = renderGraph.GetBuffer(terrainLayers.Count);

                pass.Initialize(generateIdMapMaterial);
                pass.WriteTexture(idMap, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_TerrainNormalMap", normalmap);
                pass.WriteBuffer("_ProceduralIndices", indicesBuffer);
                pass.ReadBuffer("_ProceduralIndices", indicesBuffer);
                pass.ReadBuffer("TerrainLayerData", terrainLayerData);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("LayerCount", terrainData.alphamapLayers);
                    pass.SetFloat("_Resolution", idMapResolution);
                    pass.SetVector("TerrainSize", terrain.terrainData.size);
                    pass.SetInt("_TotalLayers", terrainLayers.Count);
                    pass.SetInt("_TextureCount", terrainData.alphamapLayers);

                    // Shader supports up to 8 layers. Can easily be increased by modifying shader though
                    for (var i = 0; i < 8; i++)
                    {
                        var texture = i < terrainData.alphamapTextureCount ? terrainData.alphamapTextures[i] : Texture2D.blackTexture;
                        pass.SetTexture($"_Input{i}", texture);
                    }

                    // Need to build buffer of layer to array index
                    var layers = new NativeArray<int>(terrainLayers.Count, Allocator.Temp);
                    foreach (var layer in terrainLayers)
                    {
                        if (terrainProceduralLayers.TryGetValue(layer.Key, out var proceduralIndex))
                        {
                            // Use +1 so we can use 0 to indicate no data
                            layers[layer.Value] = proceduralIndex + 1;
                        }
                    }

                    command.SetBufferData(pass.GetBuffer(indicesBuffer), layers);
                    var tempArrayId = Shader.PropertyToID("_TempTerrainId");
                    command.SetGlobalTexture("_ExtraLayers", tempArrayId);
                });
            }
        }

        public override void Render()
        {
            // TODO: Logic here seems a bit off
            if (terrain != Terrain.activeTerrain)
                InitializeTerrain();

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
                InitializeTerrain();

            // Set this every frame incase of changes..
            // TODO: Only do when data changed?
            FillLayerData();

            var viewData = renderGraph.GetResource<ViewData>();
            var position = terrain.GetPosition() - viewData.ViewPosition;
            var size = terrainData.size;
            var terrainScaleOffset = new Vector4(1f / size.x, 1f / size.z, -position.x / size.x, -position.z / size.z);
            var terrainRemapHalfTexel = GraphicsUtilities.HalfTexelRemap(position.XZ(), size.XZ(), Vector2.one * terrainData.heightmapResolution);
            var terrainHeightOffset = position.y;
            renderGraph.SetResource(new TerrainRenderData(diffuseArray, normalMapArray, maskMapArray, heightmap, normalmap, idMap, terrainData.holesTexture, terrainRemapHalfTexel, terrainScaleOffset, size, size.y, terrainHeightOffset, terrainData.alphamapResolution, terrainLayerData));

            // This sets raytracing data on the terrain's material property block
            using (var pass = renderGraph.AddRenderPass<SetPropertyBlockPass>("Terrain Data Property Block Update"))
            {
                var propertyBlock = pass.propertyBlock;
                terrain.GetSplatMaterialPropertyBlock(propertyBlock);
                pass.AddRenderPassData<TerrainRenderData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    terrain.SetSplatMaterialPropertyBlock(propertyBlock);
                });
            }
        }
    }
}