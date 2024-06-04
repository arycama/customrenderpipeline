using Arycama.CustomRenderPipeline;
using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

public class TerrainSystem
{
    [Serializable]
    public class Settings
    {
        [field: SerializeField] public Material Material { get; private set; } = null;
        [field: SerializeField] public int CellCount { get; private set; } = 32;
        [field: SerializeField] public int PatchVertices { get; private set; } = 32;
        [field: SerializeField] public float EdgeLength { get; private set; } = 64;
    }

    private Settings settings;
    private RenderGraph renderGraph;
    private GraphicsBuffer indexBuffer, terrainLayerData;
    private RTHandle minMaxHeight, heightmap, normalmap, idMap;
    private Terrain terrain;
    private Material generateIdMapMaterial, screenSpaceTerrainMaterial;

    private Texture2DArray diffuseArray, normalMapArray, maskMapArray;
    private int VerticesPerTileEdge => settings.PatchVertices + 1;
    private int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;
    private TerrainData terrainData => terrain.terrainData;

    public TerrainSystem(RenderGraph renderGraph, Settings settings)
    {
        this.renderGraph = renderGraph;
        this.settings = settings;

        generateIdMapMaterial = new Material(Shader.Find("Hidden/Terrain Id Map")) { hideFlags = HideFlags.HideAndDontSave };
        screenSpaceTerrainMaterial = new Material(Shader.Find("Hidden/Screen Space Terrain")) { hideFlags = HideFlags.HideAndDontSave };

        TerrainCallbacks.textureChanged += TerrainCallbacks_textureChanged;
        TerrainCallbacks.heightmapChanged += TerrainCallbacks_heightmapChanged;
    }

    ~TerrainSystem()
    {
        TerrainCallbacks.textureChanged -= TerrainCallbacks_textureChanged;
        TerrainCallbacks.heightmapChanged -= TerrainCallbacks_heightmapChanged;
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

    private void InitializeTerrain()
    {
        terrain = Terrain.activeTerrain;

        if (terrain == null)
            return;

        var resolution = terrainData.heightmapResolution;
        minMaxHeight = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R16G16_UNorm, hasMips: true, isPersistent: true);
        heightmap = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R16_UNorm, isPersistent: true);
        normalmap = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R8G8_SNorm, autoGenerateMips: true, hasMips: true, isPersistent: true);

        indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, QuadListIndexCount, sizeof(ushort)) { name = "Terrain System Index Buffer" };

        var pIndices = new ushort[QuadListIndexCount];
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

        indexBuffer.SetData(pIndices);

        InitializeHeightmap();

         // Initialize terrain layers
         var layers = terrainData.terrainLayers;

        // Initialize arrays, use the first texture, since everything must be the same resolution
        // TODO: Add checks/asserts/warnings for incorrect textures
        var flags = TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate;
        diffuseArray = new Texture2DArray(layers[0].diffuseTexture.width, layers[0].diffuseTexture.width, layers.Length, layers[0].diffuseTexture.graphicsFormat, flags);
        normalMapArray = new Texture2DArray(layers[0].normalMapTexture.width, layers[0].normalMapTexture.width, layers.Length, layers[0].normalMapTexture.graphicsFormat, flags);
        maskMapArray = new Texture2DArray(layers[0].maskMapTexture.width, layers[0].maskMapTexture.width, layers.Length, layers[0].maskMapTexture.graphicsFormat, flags);

        using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Terrain Layer Data Init"))
        {
            // Add to texture array
            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                for (var i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    command.CopyTexture(layer.diffuseTexture, 0, diffuseArray, i);
                    command.CopyTexture(layer.normalMapTexture, 0, normalMapArray, i);
                    command.CopyTexture(layer.maskMapTexture, 0, maskMapArray, i);
                }
            });
        }

        // Graph doesn't support persistent buffers yet
        // TODO: Add persistent buffer support 
        //var layerDataBuffer = renderGraph.GetBuffer(layers.Length, UnsafeUtility.SizeOf<TerrainLayerData>(), GraphicsBuffer.Target.Structured);
        terrainLayerData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, layers.Length, UnsafeUtility.SizeOf<TerrainLayerData>()) { name = "Terarin Layer Data" };
        FillLayerData();

        var idMapResolution = terrainData.alphamapResolution;
        idMap = renderGraph.GetTexture(idMapResolution, idMapResolution, GraphicsFormat.R32_UInt, isPersistent: true);
        InitializeIdMap();
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

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                // Can't use pass.readTexture here since this comes from unity
                pass.SetTexture(command, "HeightmapInput", terrainData.heightmapTexture);
            });
        }

        // Initialize normal map
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Terrain Normalmap Init"))
        {
            pass.Initialize(computeShader, 1, resolution, resolution);
            pass.WriteTexture("InitNormalMapOutput", normalmap, 0);

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                // Can't use pass.readTexture here since this comes from unity
                pass.SetTexture(command, "InitNormalMapInput", terrainData.heightmapTexture);

                var scale = terrainData.heightmapScale;
                var height = terrainData.size.y;
                pass.SetInt(command, "MaxWidth", terrainData.heightmapResolution - 1);
                pass.SetInt(command, "MaxHeight", terrainData.heightmapResolution - 1);
                pass.SetVector(command, "Scale", new Vector2(height / (8f * scale.x), height / (8f * scale.z)));
            });
        }

        // Generate min/max for terrain.
        // Todo: Could generate this as part of heightmap pass using LDS
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Terrain Min Max Height Init"))
        {
            pass.Initialize(computeShader, 2, resolution, resolution);
            pass.WriteTexture("DepthCopyResult", minMaxHeight, 0);

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                pass.SetTexture(command, "DepthCopyInput", heightmap);
            });
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

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    var prevWidth = Mathf.Max(1, resolution >> (index - 1));
                    var prevHeight = Mathf.Max(1, resolution >> (index - 1));

                    pass.SetInt(command, "_Width", prevWidth);
                    pass.SetInt(command, "_Height", prevHeight);
                });
            }
        }
    }

    private void FillLayerData()
    {
        using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Terrain Layer Data Init"))
        {
            var layers = terrainData.terrainLayers;
            var layerData = terrainLayerData.LockBufferForWrite<TerrainLayerData>(0, layers.Length);
            for (var i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                layerData[i] = new TerrainLayerData(layer.tileSize.x, Mathf.Max(1e-3f, layer.smoothness), layer.normalScale, 1.0f - layer.metallic);
            }

            terrainLayerData.UnlockBufferAfterWrite<TerrainLayerData>(layers.Length);
        }
    }

    private void InitializeIdMap()
    {
        var idMapResolution = terrainData.alphamapResolution;

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Terrain Layer Data Init"))
        {
            pass.Initialize(generateIdMapMaterial);
            pass.WriteTexture(idMap, RenderBufferLoadAction.DontCare);
            pass.ReadTexture("_TerrainNormalMap", normalmap);

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                pass.SetInt(command, "LayerCount", terrainData.alphamapLayers);
                pass.SetFloat(command, "_Resolution", idMapResolution);
                pass.SetBuffer(command, "TerrainLayerData", terrainLayerData);
                pass.SetVector(command, "TerrainSize", terrain.terrainData.size);

                // Shader supports up to 8 layers. Can easily be increased by modifying shader though
                for (var i = 0; i < 8; i++)
                {
                    var texture = i < terrainData.alphamapTextureCount ? terrainData.alphamapTextures[i] : Texture2D.blackTexture;
                    pass.SetTexture(command, $"_Input{i}", texture);
                }
            });
        }
    }

    public void Update()
    {
        if (terrain != Terrain.activeTerrain)
            InitializeTerrain();

        if (terrain == null)
            return;

        // Set this every frame incase of changes..
        // TODO: Only do when data changed?
        FillLayerData();
    }

    public void SetupRenderData(Vector3 viewPosition)
    {
        if (terrain == null)
            return;

        var position = terrain.GetPosition() - viewPosition;
        var size = terrainData.size;
        var terrainScaleOffset = new Vector4(1f / size.x, 1f / size.z, -position.x / size.x, -position.z / size.z);
        var terrainRemapHalfTexel = GraphicsUtilities.HalfTexelRemap(position.XZ(), size.XZ(), Vector2.one * terrainData.heightmapResolution);
        var terrainHeightOffset = position.y;
        renderGraph.ResourceMap.SetRenderPassData(new TerrainRenderData(diffuseArray, normalMapArray, maskMapArray, heightmap, normalmap, idMap, terrainData.holesTexture, terrainRemapHalfTexel, terrainScaleOffset, size, size.y, terrainHeightOffset, terrainData.alphamapResolution, terrainLayerData), renderGraph.FrameIndex);

        // This sets raytracing data on the terrain's material property block
        using (var pass = renderGraph.AddRenderPass<SetPropertyBlockPass>("Terrain Data Property Block Update"))
        {
            var propertyBlock = pass.propertyBlock;
            terrain.GetSplatMaterialPropertyBlock(propertyBlock);
            pass.AddRenderPassData<TerrainRenderData>();

            var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
            {
                terrain.SetSplatMaterialPropertyBlock(propertyBlock);
            });
        }
    }

    struct CullResult
    {
        public BufferHandle IndirectArgsBuffer { get; }
        public BufferHandle PatchDataBuffer { get; }

        public CullResult(BufferHandle indirectArgsBuffer, BufferHandle patchDataBuffer)
        {
            IndirectArgsBuffer = indirectArgsBuffer ?? throw new ArgumentNullException(nameof(indirectArgsBuffer));
            PatchDataBuffer = patchDataBuffer ?? throw new ArgumentNullException(nameof(patchDataBuffer));
        }
    }

    private CullResult Cull(Vector3 viewPosition, CullingPlanes cullingPlanes, ICommonPassData commonPassData)
    {
        // TODO: Preload?
        var compute = Resources.Load<ComputeShader>("Terrain/TerrainQuadtreeCull");
        var indirectArgsBuffer = renderGraph.GetBuffer(5, target: GraphicsBuffer.Target.IndirectArguments);
        var patchDataBuffer = renderGraph.GetBuffer(settings.CellCount * settings.CellCount, target: GraphicsBuffer.Target.Structured);

        // We can do 32x32 cells in a single pass, larger counts need to be broken up into several passes
        var maxPassesPerDispatch = 6;
        var totalPassCount = (int)Mathf.Log(settings.CellCount, 2f) + 1;
        var dispatchCount = Mathf.Ceil(totalPassCount / (float)maxPassesPerDispatch);

        RTHandle tempLodId = null;
        BufferHandle lodIndirectArgsBuffer = null;
        if (dispatchCount > 1)
        {
            // If more than one dispatch, we need to write lods out to a temp texture first. Otherwise they are done via shared memory so no texture is needed
            tempLodId = renderGraph.GetTexture(settings.CellCount, settings.CellCount, GraphicsFormat.R16_UInt);
            lodIndirectArgsBuffer = renderGraph.GetBuffer(3, target: GraphicsBuffer.Target.IndirectArguments);
        }

        var tempIds = ListPool<RTHandle>.Get();
        for (var i = 0; i < dispatchCount - 1; i++)
        {
            var tempResolution = 1 << ((i + 1) * (maxPassesPerDispatch - 1));
            tempIds.Add(renderGraph.GetTexture(tempResolution, tempResolution, GraphicsFormat.R16_UInt));
        }

        for (var i = 0; i < dispatchCount; i++)
        {
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Terrain Quadtree Cull"))
            {
                // I don't think this is required.
                commonPassData.SetInputs(pass);

                var isFirstPass = i == 0; // Also indicates whether this is -not- the first pass
                if (!isFirstPass)
                    pass.ReadTexture("_TempResult", tempIds[i - 1]);

                var isFinalPass = i == dispatchCount - 1; // Also indicates whether this is -not- the final pass

                var passCount = Mathf.Min(maxPassesPerDispatch, totalPassCount - i * maxPassesPerDispatch);
                var threadCount = 1 << (i * 6 + passCount - 1);
                pass.Initialize(compute, 0, threadCount, threadCount);

                if (isFirstPass)
                    pass.AddKeyword("FIRST");

                if (isFinalPass)
                    pass.AddKeyword("FINAL");

                if (isFinalPass && !isFirstPass)
                {
                    // Final pass writes out lods to a temp texture if more than one pass was used
                    pass.WriteTexture("_LodResult", tempLodId);
                }

                if (!isFinalPass)
                    pass.WriteTexture("_TempResultWrite", tempIds[i]);

                pass.WriteBuffer("_IndirectArgs", indirectArgsBuffer);
                pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);
                pass.ReadTexture("_TerrainHeights", minMaxHeight);

                var index = i;
                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    // First pass sets the buffer contents
                    if (isFirstPass)
                    {
                        var indirectArgs = ListPool<int>.Get();
                        indirectArgs.Add(QuadListIndexCount); // index count per instance
                        indirectArgs.Add(0); // instance count (filled in later)
                        indirectArgs.Add(0); // start index location
                        indirectArgs.Add(0); // base vertex location
                        indirectArgs.Add(0); // start instance location
                        command.SetBufferData(indirectArgsBuffer, indirectArgs);
                        ListPool<int>.Release(indirectArgs);
                    }

                    commonPassData.SetProperties(pass, command);

                    // Do up to 6 passes per dispatch.
                    pass.SetInt(command, "_PassCount", passCount);
                    pass.SetInt(command, "_PassOffset", 6 * index);
                    pass.SetInt(command, "_TotalPassCount", totalPassCount);

                    var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
                    for(var i = 0; i < cullingPlanes.Count; i++)
                        cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

                    pass.SetVectorArray(command, "_CullingPlanes", cullingPlanesArray);
                    ArrayPool<Vector4>.Release(cullingPlanesArray);

                    // Snap to quad-sized increments on largest cell
                    var position = terrain.GetPosition() - viewPosition;
                    var positionOffset = new Vector4(terrainData.size.x, terrainData.size.z, position.x, position.z);
                    pass.SetVector(command, "_TerrainPositionOffset", positionOffset);

                    pass.SetFloat(command, "_EdgeLength", (float)settings.EdgeLength * settings.PatchVertices);
                    pass.SetInt(command, "_CullingPlanesCount", cullingPlanes.Count);

                    pass.SetFloat(command, "_InputScale", terrainData.size.y);
                    pass.SetFloat(command, "_InputOffset", position.y);

                    pass.SetInt(command, "_MipCount", Texture2DExtensions.MipCount(terrainData.heightmapResolution) - 1);
                });
            }
        }

        if (dispatchCount > 1)
        {
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Terrain Quadtree Cull"))
            {
                pass.Initialize(compute, 1, normalizedDispatch: false);
                pass.WriteBuffer("_IndirectArgs", lodIndirectArgsBuffer);

                // If more than one pass needed, we need a second pass to write out lod deltas to the patch data
                // Copy count from indirect draw args so we only dispatch as many threads as needed
                pass.ReadBuffer("_IndirectArgsInput", indirectArgsBuffer);
            }

            using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Terrain Quadtree Cull"))
            {
                pass.Initialize(compute, lodIndirectArgsBuffer, 2);
                pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);
                pass.ReadTexture("_LodInput", tempLodId);
                pass.ReadBuffer("_IndirectArgs", indirectArgsBuffer);

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    pass.SetInt(command, "_CellCount", settings.CellCount);
                });
            }
        }

        ListPool<RTHandle>.Release(tempIds);

        return new(indirectArgsBuffer, patchDataBuffer);
    }

    public void CullShadow(Vector3 viewPosition, CullingPlanes cullingPlanes, ICommonPassData commonPassData)
    {
        if (terrain == null)
            return;

        var cullingResult = Cull(viewPosition, cullingPlanes, commonPassData);
        renderGraph.ResourceMap.SetRenderPassData(new TerrainShadowCullResult(cullingResult.IndirectArgsBuffer, cullingResult.PatchDataBuffer), renderGraph.FrameIndex);
    }

    public void CullRender(Vector3 viewPosition, CullingPlanes cullingPlanes, ICommonPassData commonPassData)
    {
        if (terrain == null)
            return;

        var cullingResult = Cull(viewPosition, cullingPlanes, commonPassData);
        renderGraph.ResourceMap.SetRenderPassData(new TerrainRenderCullResult(cullingResult.IndirectArgsBuffer, cullingResult.PatchDataBuffer), renderGraph.FrameIndex);
    }

    public void Render(string passName, Vector3 viewPosititon, RTHandle cameraDepth, CullingPlanes cullingPlanes, ICommonPassData commonPassData, ScriptableRenderContext context, Camera camera, CullingResults cullingResults)
    {
        if (terrain == null || settings.Material == null)
            return;

        var passIndex = settings.Material.FindPass(passName);
        Assert.IsFalse(passIndex == -1, "Terrain Material has no Terrain Pass");

        var size = terrainData.size;
        var position = terrain.GetPosition() - viewPosititon;
        var passData = renderGraph.ResourceMap.GetRenderPassData<TerrainRenderCullResult>(renderGraph.FrameIndex);

        using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Terrain Render"))
        {
            pass.WriteDepth(cameraDepth, RenderTargetFlags.None, RenderBufferLoadAction.DontCare);

            pass.Initialize(settings.Material, indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);
            pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);

            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<TerrainRenderData>();
            commonPassData.SetInputs(pass);

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);

                pass.SetInt(command, "_VerticesPerEdge", VerticesPerTileEdge);
                pass.SetInt(command, "_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
                pass.SetFloat(command, "_RcpVerticesPerEdge", 1f / VerticesPerTileEdge);
                pass.SetFloat(command, "_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));

                var scaleOffset = new Vector4(size.x / settings.CellCount, size.z / settings.CellCount, position.x, position.z);
                pass.SetVector(command, "_PatchScaleOffset", scaleOffset);
                pass.SetVector(command, "_SpacingScale", new Vector4(size.x / settings.CellCount / settings.PatchVertices, size.z / settings.CellCount / settings.PatchVertices, position.x, position.z));
                pass.SetFloat(command, "_PatchUvScale", 1f / settings.CellCount);

                pass.SetFloat(command, "_HeightUvScale", 1f / settings.CellCount * (1.0f - 1f / terrainData.heightmapResolution));
                pass.SetFloat(command, "_HeightUvOffset", 0.5f / terrainData.heightmapResolution);

                pass.SetFloat(command, "_MaxLod", Mathf.Log(settings.CellCount, 2));

                pass.SetInt(command, "_CullingPlanesCount", cullingPlanes.Count);
                var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
                for (var i = 0; i < cullingPlanes.Count; i++)
                    cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

                pass.SetVectorArray(command, "_CullingPlanes", cullingPlanesArray);
                ArrayPool<Vector4>.Release(cullingPlanesArray);
            });
        }

        using (var pass = renderGraph.AddRenderPass<ObjectRenderPass>("Render Terrain Replacement"))
        {
            pass.Initialize("Terrain", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.None, true);
            pass.WriteDepth(cameraDepth, RenderTargetFlags.None);
            commonPassData.SetInputs(pass);

            var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
            });
        }
    }

    public void RenderShadow(Vector3 viewPosition, RTHandle shadow, CullingPlanes cullingPlanes, ICommonPassData commonPassData, Matrix4x4 worldToClip, int cascadeIndex, float bias, float slopeBias)
    {
        if (terrain == null || settings.Material == null)
            return;

        var passIndex = settings.Material.FindPass("ShadowCaster");
        Assert.IsFalse(passIndex == -1, "Terrain Material has no ShadowCaster Pass");

        var size = terrainData.size;
        var position = terrain.GetPosition() - viewPosition;
        var passData = renderGraph.ResourceMap.GetRenderPassData<TerrainShadowCullResult>(renderGraph.FrameIndex);

        using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Terrain Render"))
        {
            pass.Initialize(settings.Material, indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex, null, bias, slopeBias, false);

            pass.WriteTexture(shadow);
            pass.DepthSlice = cascadeIndex;

            pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);
            commonPassData.SetInputs(pass);
            pass.AddRenderPassData<TerrainRenderData>();
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);

                pass.SetInt(command, "_VerticesPerEdge", VerticesPerTileEdge);
                pass.SetInt(command, "_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
                pass.SetFloat(command, "_RcpVerticesPerEdge", 1f / VerticesPerTileEdge);
                pass.SetFloat(command, "_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));

                var scaleOffset = new Vector4(size.x / settings.CellCount, size.z / settings.CellCount, position.x, position.z);
                pass.SetVector(command, "_PatchScaleOffset", scaleOffset);
                pass.SetVector(command, "_SpacingScale", new Vector4(size.x / settings.CellCount / settings.PatchVertices, size.z / settings.CellCount / settings.PatchVertices, position.x, position.z));
                pass.SetFloat(command, "_PatchUvScale", 1f / settings.CellCount);

                pass.SetFloat(command, "_HeightUvScale", 1f / settings.CellCount * (1.0f - 1f / terrainData.heightmapResolution));
                pass.SetFloat(command, "_HeightUvOffset", 0.5f / terrainData.heightmapResolution);

                pass.SetFloat(command, "_MaxLod", Mathf.Log(settings.CellCount, 2));
                pass.SetInt(command, "_CullingPlanesCount", cullingPlanes.Count);

                var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
                for (var i = 0; i < cullingPlanes.Count; i++)
                    cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

                pass.SetVectorArray(command, "_CullingPlanes", cullingPlanesArray);
                ArrayPool<Vector4>.Release(cullingPlanesArray);

                pass.SetMatrix(command, "_WorldToClip", worldToClip);
            });
        }
    }

    public void RenderTerrainScreenspace(Camera camera, RTHandle cameraDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, IRenderPassData commonPassData)
    {
        if (!renderGraph.ResourceMap.IsRenderPassDataValid<TerrainRenderData>(renderGraph.FrameIndex))
            return;

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Terrain Screen Pass"))
        {
            pass.Initialize(screenSpaceTerrainMaterial, 0, 1, null, camera);
            pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(albedoMetallic);
            pass.WriteTexture(normalRoughness);
            pass.WriteTexture(bentNormalOcclusion);
            pass.ReadTexture("_Depth", cameraDepth);
            commonPassData.SetInputs(pass);

            pass.AddRenderPassData<TerrainRenderData>();

            var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
            });
        }
    }

    private class PassData
    {
    }
}

public struct TerrainRenderCullResult : IRenderPassData
{
    public BufferHandle IndirectArgsBuffer { get; }
    public BufferHandle PatchDataBuffer { get; }

    public TerrainRenderCullResult(BufferHandle indirectArgsBuffer, BufferHandle patchDataBuffer)
    {
        IndirectArgsBuffer = indirectArgsBuffer ?? throw new ArgumentNullException(nameof(indirectArgsBuffer));
        PatchDataBuffer = patchDataBuffer ?? throw new ArgumentNullException(nameof(patchDataBuffer));
    }

    public readonly void SetInputs(RenderPass pass)
    {
        pass.ReadBuffer("_PatchData", PatchDataBuffer);
    }

    public readonly void SetProperties(RenderPass pass, CommandBuffer command)
    {
    }
}

public struct TerrainShadowCullResult : IRenderPassData
{
    public BufferHandle IndirectArgsBuffer { get; }
    public BufferHandle PatchDataBuffer { get; }

    public TerrainShadowCullResult(BufferHandle indirectArgsBuffer, BufferHandle patchDataBuffer)
    {
        IndirectArgsBuffer = indirectArgsBuffer ?? throw new ArgumentNullException(nameof(indirectArgsBuffer));
        PatchDataBuffer = patchDataBuffer ?? throw new ArgumentNullException(nameof(patchDataBuffer));
    }

    public readonly void SetInputs(RenderPass pass)
    {
        pass.ReadBuffer("_PatchData", PatchDataBuffer);
    }

    public readonly void SetProperties(RenderPass pass, CommandBuffer command)
    {
    }
}

public struct TerrainLayerData
{
    private readonly float scale;
    private readonly float blending;
    private readonly float stochastic;
    private readonly float rotation;

    public TerrainLayerData(float scale, float blending, float stochastic, float rotation)
    {
        this.scale = scale;
        this.blending = blending;
        this.stochastic = stochastic;
        this.rotation = rotation;
    }
}
