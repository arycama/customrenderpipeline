using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using Unmath;
using static Unmath.Math;
using Object = UnityEngine.Object;

public class TerrainSystem : FrameRenderFeature
{
    private static readonly int HeightmapInputId = Shader.PropertyToID("HeightmapInput");
    private static readonly int InitNormalMapInputId = Shader.PropertyToID("InitNormalMapInput");
    private static readonly int TerrainHeightmapId = Shader.PropertyToID("TerrainHeightmap");

    private readonly TerrainSettings settings;

    private readonly Material generateIdMapMaterial, terrainAmbientOcclusionMaterial;
    private readonly ComputeShader heightmapComputeShader;

    private readonly Dictionary<TerrainLayer, int> terrainLayers = new();

    private Texture2DArray diffuseArray, normalMapArray, maskMapArray;

    private ResourceHandle<GraphicsBuffer> terrainLayerData, indexBuffer;
    private ResourceHandle<RenderTexture> minMaxHeight, heightmap, normalmap, idMap, aoMap;
    private float maxHeightExtents;

    private RectInt heightmapUpdateRect = new(), idMapUpdateRect = new();
    private int previousHeightmapVersion, previousIdMapVersion;
    private bool aoMapGenerated;

    public int HeightmapSize { get; private set; }
    public int IdMapSize { get; private set; }
    public int HeightmapVersion { get; private set; }
    public int IdMapVersion { get; private set; }
    public RectInt LastHeightmapUpdateRect { get; private set; }
    public RectInt LastIdUpdateRect { get; private set; }
    public Terrain Terrain { get; private set; }
    private TerrainData TerrainData => Terrain.terrainData;

    public TerrainSystem(RenderGraph renderGraph, TerrainSettings settings) : base(renderGraph)
    {
        this.settings = settings;

        generateIdMapMaterial = new Material(Shader.Find("Hidden/Terrain Id Map")) { hideFlags = HideFlags.HideAndDontSave };
        terrainAmbientOcclusionMaterial = new Material(Shader.Find("Hidden/Terrain Ambient Occlusion")) { hideFlags = HideFlags.HideAndDontSave };
        heightmapComputeShader = Resources.Load<ComputeShader>("Terrain/InitTerrain");

        TerrainCallbacks.textureChanged += TextureModified;
        TerrainCallbacks.heightmapChanged += HeightmapModified;
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
        else if (Terrain == null)
            return;

        UpdateLayerData();

        var size = (Float3)TerrainData.size;
        var terrainFrameData = renderGraph.SetConstantBuffer
        ((
            size,
            (float)TerrainData.alphamapResolution,
            GraphicsUtilities.HalfTexelRemap(TerrainData.heightmapResolution),
            size.y,
            (float)TerrainData.heightmapResolution,
            maxHeightExtents
        ));

        if (previousHeightmapVersion != HeightmapVersion)
        {
            UpdateHeightmap(heightmapUpdateRect);

            // If this is the first update, also generate the AO map
            if (!aoMapGenerated)
            {
                UpdateAoMap();
                aoMapGenerated = true;
            }

            LastHeightmapUpdateRect = heightmapUpdateRect;
            heightmapUpdateRect = RectInt.zero;
            previousHeightmapVersion = HeightmapVersion;
        }

        renderGraph.SetResource<TerrainFrameData>
        (new(
            diffuseArray,
            normalMapArray,
            maskMapArray,
            heightmap,
            normalmap,
            idMap,
            TerrainData.holesTexture,
            terrainLayerData,
            aoMap,
            terrainFrameData
        ));

        if (previousIdMapVersion != IdMapVersion)
        {
            UpdateIdMap(idMapUpdateRect);
            LastIdUpdateRect = idMapUpdateRect;
            idMapUpdateRect = RectInt.zero;
            previousIdMapVersion = IdMapVersion;
        }
    }

    protected override void Cleanup(bool disposing)
    {
        TerrainCallbacks.textureChanged -= TextureModified;
        TerrainCallbacks.heightmapChanged -= HeightmapModified;

        if (diffuseArray != null)
            Object.DestroyImmediate(diffuseArray);

        if (normalMapArray != null)
            Object.DestroyImmediate(normalMapArray);

        if (maskMapArray != null)
            Object.DestroyImmediate(maskMapArray);

        if (Terrain != null)
            CleanupResources();
    }

    private void CleanupResources()
    {
        renderGraph.ReleasePersistentResource(minMaxHeight, -1);
        renderGraph.ReleasePersistentResource(heightmap, -1);
        renderGraph.ReleasePersistentResource(normalmap, -1);
        renderGraph.ReleasePersistentResource(idMap, -1);
        renderGraph.ReleasePersistentResource(aoMap, -1);
        Object.DestroyImmediate(diffuseArray);
        Object.DestroyImmediate(normalMapArray);
        Object.DestroyImmediate(maskMapArray);
        renderGraph.ReleasePersistentResource(terrainLayerData, -1);
        renderGraph.ReleasePersistentResource(indexBuffer, -1);
    }

    private void QueueHeightmapUpdate(RectInt region)
    {
        HeightmapVersion++;
        heightmapUpdateRect = heightmapUpdateRect.Encapsulate(region);
    }

    private void QueueIdMapUpdate(RectInt region)
    {
        IdMapVersion++;
        idMapUpdateRect = idMapUpdateRect.Encapsulate(region);
    }

    private void HeightmapModified(Terrain terrain, RectInt region, bool synched)
    {
        if (terrain != Terrain)
            return;

        QueueHeightmapUpdate(region);
        QueueIdMapUpdate(region);
    }

    private void TextureModified(Terrain terrain, string textureName, RectInt region, bool synched)
    {
        if (terrain != Terrain || textureName != TerrainData.AlphamapTextureName)
            return;

        QueueIdMapUpdate(region);
    }

    private void InitializeTerrain()
    {
        HeightmapSize = TerrainData.heightmapResolution;
        IdMapSize = TerrainData.alphamapResolution;

        minMaxHeight = renderGraph.GetTexture(HeightmapSize, GraphicsFormat.R16G16_UNorm, hasMips: true, isPersistent: true);
        heightmap = renderGraph.GetTexture(HeightmapSize, GraphicsFormat.R16_UNorm, hasMips: true, autoGenerateMips: true, isPersistent: true);
        normalmap = renderGraph.GetTexture(HeightmapSize, GraphicsFormat.R8G8_SNorm, autoGenerateMips: true, hasMips: true, isPersistent: true);
        indexBuffer = renderGraph.GetGridIndexBuffer(settings.PatchVertices, true, false, true);

        idMap = renderGraph.GetTexture(IdMapSize, GraphicsFormat.R32_UInt, isPersistent: true);
        aoMap = renderGraph.GetTexture(HeightmapSize, GraphicsFormat.R8G8B8A8_SNorm, isPersistent: true);

        // Initialize terrain layers
        terrainLayers.Clear();
        var layers1 = TerrainData.terrainLayers;
        if (layers1 != null)
        {
            for (var i = 0; i < layers1.Length; i++)
            {
                terrainLayers.Add(layers1[i], i);
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

        var layerCount = terrainLayers.Count;
        var arraySize = Max(1, layerCount);

        // Initialize arrays, use the first texture, since everything must be the same resolution
        // Create texture arrays and copy textures. (Initialize with defaults if empty to avoid errors
        var flags = TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate;
        diffuseArray = new(diffuseWidth, diffuseHeight, arraySize, diffuseFormat == GraphicsFormat.None ? GraphicsFormat.R8_UNorm : diffuseFormat, flags) { name = "Terrain Diffuse" };
        diffuseArray.Apply(false, true);
        normalMapArray = new(normalWidth, normalHeight, arraySize, normalFormat == GraphicsFormat.None ? GraphicsFormat.R8_UNorm : normalFormat, flags) { name = "Terrain Normal" };
        normalMapArray.Apply(false, true);
        maskMapArray = new(maskWidth, maskHeight, arraySize, maskFormat == GraphicsFormat.None ? GraphicsFormat.R8_UNorm : maskFormat, flags) { name = "Terrain Mask" };
        maskMapArray.Apply(false, true);

        terrainLayerData = renderGraph.GetBuffer(arraySize, isPersistent: true);

        using (var pass = renderGraph.AddGenericRenderPass("Terrain Texture Array Update", (terrainLayers, diffuseArray, normalMapArray, maskMapArray)))
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

        QueueHeightmapUpdate(new(0, 0, HeightmapSize, HeightmapSize));
        aoMapGenerated = false;
        QueueIdMapUpdate(new(0, 0, IdMapSize, IdMapSize));
        renderGraph.SetResource(new TerrainSystemData(minMaxHeight, Terrain, TerrainData, indexBuffer), true);
    }

    private void UpdateLayerData()
    {
        var count = terrainLayers.Count;
        maxHeightExtents = 0f;
        if (count == 0)
            return;

        var layerData = ListPool<int>.Get();
        foreach (var layer in terrainLayers.Keys)
        {
            var heightScale = layer.metallic;
            var stochasticScale = layer.normalScale;
            var opacity = layer.smoothness;
            var translucency = layer.specular.a;
            var scale = Rcp(layer.tileSize.x);

            var extinction = heightScale > 0.0f ? Rcp(Square(Max(1e-3f, 1.0f - Max(0.1f, opacity)))) / heightScale : 0.0f;

            // Pack values together
            var maxScale = 4;
            var maxHeight = 0.32f;
            var normalizedExtinction = Min(65504.0f, extinction) / 65504.0f;
            var packedData = Packing.BitPackFloat(normalizedExtinction, 12, 0);
            packedData |= Packing.BitPackFloat(scale / maxScale, 5, 12);
            packedData |= Packing.BitPackFloat(heightScale / maxHeight, 5, 17);
            packedData |= Packing.BitPackFloat(translucency, 5, 22);
            packedData |= Packing.BitPackFloat(stochasticScale, 5, 27);

            layerData.Add(packedData);

            maxHeightExtents = Max(maxHeightExtents, heightScale * 0.5f);
        }

        using (var pass = renderGraph.AddGenericRenderPass("Terrain Layer Data Update", (terrainLayerData, layerData)))
        {
            pass.WriteBuffer("", terrainLayerData);
            pass.SetRenderFunction(static (command, pass, data) =>
            {
                command.SetBufferData(pass.GetBuffer(data.terrainLayerData), data.layerData);
                ListPool<int>.Release(data.layerData);
            });
        }
    }

    private void UpdateHeightmap(RectInt? region)
    {
        // Initialize the height map
        using (var pass = renderGraph.AddComputeRenderPass("Terrain Heightmap Init", TerrainData.heightmapTexture))
        {
            pass.Initialize(heightmapComputeShader, 0, HeightmapSize, HeightmapSize);
            pass.WriteTexture("HeightmapResult", heightmap, 0);

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                // Can't use pass.readTexture here since this comes from unity
                pass.SetTexture(HeightmapInputId, data);
            });
        }

        // Initialize normal map
        // TODO: Shuold use regular RTs for better DCC?
        using (var pass = renderGraph.AddComputeRenderPass("Terrain Normalmap Init", (TerrainData.heightmapTexture, TerrainData.heightmapScale, TerrainData.size.y, HeightmapSize)))
        {
            pass.Initialize(heightmapComputeShader, 1, HeightmapSize, HeightmapSize);
            pass.WriteTexture("InitNormalMapOutput", normalmap, 0);

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                // Can't use pass.readTexture here since this comes from unity
                pass.SetTexture(InitNormalMapInputId, data.heightmapTexture);

                var scale = data.heightmapScale;
                var height = data.y;
                pass.SetInt("MaxWidth", data.HeightmapSize - 1);
                pass.SetInt("MaxHeight", data.HeightmapSize - 1);
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
        // TODO: Shuold use regular RTs for better DCC?
        using (var pass = renderGraph.AddComputeRenderPass("Terrain Min Max Height Init"))
        {
            pass.Initialize(heightmapComputeShader, 2, HeightmapSize, HeightmapSize);
            pass.WriteTexture("DepthCopyResult", minMaxHeight, 0);
            pass.ReadTexture("DepthCopyInput", heightmap);
        }

        var mipCount = Texture2DExtensions.MipCount(HeightmapSize);
        for (var i = 1; i < mipCount; i++)
        {
            var index = i;
            using (var pass = renderGraph.AddComputeRenderPass("Terrain Min Max Height", (resolution: HeightmapSize, index)))
            {
                // For some resolutions, we can end up with a 1x2, followed by a 1x1.
                var xSize = Max(1, HeightmapSize >> index);
                var ySize = Max(1, HeightmapSize >> index);

                pass.Initialize(heightmapComputeShader, 3, xSize, ySize);
                pass.WriteTexture("GenerateMinMaxHeightsResult", minMaxHeight, index);
                pass.WriteTexture("GenerateMinMaxHeightsInput", minMaxHeight, index - 1);

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    var prevWidth = Max(1, data.resolution >> (data.index - 1));
                    var prevHeight = Max(1, data.resolution >> (data.index - 1));

                    pass.SetInt("_Width", prevWidth);
                    pass.SetInt("_Height", prevHeight);
                });
            }
        }
    }

    private void UpdateIdMap(RectInt region)
    {
        if (terrainLayers.Count == 0)
            return;

        var viewport = new Rect(new Vector2(region.position.x, IdMapSize - region.position.y - region.size.y), region.size);
        var layerCount = terrainLayers.Count;

        using (var pass = renderGraph.AddFullscreenRenderPass("Terrain Id Map Update", (TerrainData.alphamapLayers, (Float3)Terrain.terrainData.size, terrainLayers.Count, TerrainData.alphamapLayers, TerrainData.alphamapTextureCount, TerrainData.alphamapTextures, viewport)))
        {
            pass.Initialize(generateIdMapMaterial, IdMapSize);
            pass.WriteTexture(idMap);
            pass.ReadTexture("TerrainNormalMap", normalmap);
            pass.ReadBuffer("TerrainLayerData", terrainLayerData);
            pass.ReadResource<TerrainFrameData>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetInt("LayerCount", data.Item1);
                pass.SetVector("TerrainSize", data.Item2);
                pass.SetInt("TotalLayers", data.Count);
                pass.SetInt("TextureCount", data.Item4);
                command.EnableScissorRect(data.viewport);

                // Shader supports up to 8 layers. Can easily be increased by modifying shader though
                for (var i = 0; i < 8; i++)
                {
                    var texture = i < data.alphamapTextureCount ? data.alphamapTextures[i] : Texture2D.blackTexture;
                    pass.SetTexture(Shader.PropertyToID($"Input{i}"), texture);
                }
            });
        }

        using (var pass = renderGraph.AddGenericRenderPass("Terrain Id Map Update"))
        {
            pass.SetRenderFunction(static (command, pass) =>
            {
                command.DisableScissorRect();
            });
        }
    }

    private void UpdateAoMap(RectInt? texelRegion = null)
    {
        using var pass = renderGraph.AddFullscreenRenderPass("Terrain Update Ao Map", (TerrainData, settings, HeightmapSize));

        pass.Initialize(terrainAmbientOcclusionMaterial, HeightmapSize);
        pass.WriteTexture(aoMap);

        pass.ReadTexture("TerrainHeightmap", heightmap);
        pass.ReadTexture("TerarinNormalMap", normalmap);

        pass.SetRenderFunction(static (command, pass, data) =>
        {
            pass.SetTexture(TerrainHeightmapId, data.TerrainData.heightmapTexture);
            pass.SetFloat("DirectionCount", data.settings.AmbientOcclusionDirections);
            pass.SetFloat("SampleCount", data.settings.AmbientOcclusionSamples);
            pass.SetFloat("Radius", data.settings.AmbientOcclusionRadius / data.TerrainData.size.x);
            pass.SetFloat("Resolution", data.HeightmapSize);
            pass.SetVector("TerrainSize", data.TerrainData.size);
        });
    }
}