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
        [field: SerializeField] public int CellCount { get; private set; } = 32;
        [field: SerializeField] public int PatchVertices { get; private set; } = 32;
        [field: SerializeField] public float EdgeLength { get; private set; } = 64;
    }

    private Settings settings;
    private RenderGraph renderGraph;
    private GraphicsBuffer indexBuffer, terrainLayerData;
    private RTHandle minMaxHeight, heightmap, normalmap, idMap;
    private Terrain terrain;
    private Material generateIdMapMaterial;

    private Texture2DArray diffuseArray, normalMapArray, maskMapArray;

    private int VerticesPerTileEdge => settings.PatchVertices + 1;
    private int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;
    private TerrainData terrainData => terrain.terrainData;

    public TerrainSystem(RenderGraph renderGraph, Settings settings)
    {
        this.renderGraph = renderGraph;
        this.settings = settings;

        generateIdMapMaterial = new Material(Shader.Find("Hidden/Terrain Id Map")) { hideFlags = HideFlags.HideAndDontSave };
    }

    private void InitializeTerrain()
    {
        terrain = Terrain.activeTerrain;

        var resolution = terrainData.heightmapResolution;
        minMaxHeight = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R16G16_UNorm, hasMips: true, isPersistent: true);
        heightmap = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R16_UNorm, isPersistent: true);
        normalmap = renderGraph.GetTexture(resolution, resolution, GraphicsFormat.R8G8_SNorm, autoGenerateMips: true, hasMips: true, isPersistent: true);

        indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, QuadListIndexCount, sizeof(ushort));

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

        // Initialize terrain layers
        var layers = terrainData.terrainLayers;

        // Initialize arrays, use the first texture, since everything must be the same resolution
        // TODO: Add checks/asserts/warnings for incorrect textures
        var flags = TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate;
        diffuseArray = new Texture2DArray(layers[0].diffuseTexture.width, layers[0].diffuseTexture.width, layers.Length, layers[0].diffuseTexture.graphicsFormat, flags);
        normalMapArray = new Texture2DArray(layers[0].normalMapTexture.width, layers[0].normalMapTexture.width, layers.Length, layers[0].normalMapTexture.graphicsFormat, flags);
        maskMapArray = new Texture2DArray(layers[0].maskMapTexture.width, layers[0].maskMapTexture.width, layers.Length, layers[0].maskMapTexture.graphicsFormat, flags);

        // Graph doesn't support persistent buffers yet
        // TODO: Add persistent buffer support 
        //var layerDataBuffer = renderGraph.GetBuffer(layers.Length, UnsafeUtility.SizeOf<TerrainLayerData>(), GraphicsBuffer.Target.Structured);
        terrainLayerData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, layers.Length, UnsafeUtility.SizeOf<TerrainLayerData>());
        using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Terrain Layer Data Init"))
        {
            // Only required so that the resource gets created.. I think
            // TODO: Uncomment this one persistent buffer support is added
            //pass.WriteBuffer("TerrainLayerData", layerDataBuffer);

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                var layerData = ArrayPool<TerrainLayerData>.Get(layers.Length);
                for (var i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    layerData[i] = new TerrainLayerData(terrainData.size.x / layer.tileSize.x, layer.smoothness, layer.normalScale, 1.0f - layer.metallic);

                    // Add to texture array
                    command.CopyTexture(layer.diffuseTexture, 0, diffuseArray, i);
                    command.CopyTexture(layer.normalMapTexture, 0, normalMapArray, i);
                    command.CopyTexture(layer.maskMapTexture, 0, maskMapArray, i);
                }

                command.SetBufferData(terrainLayerData, layerData);
                ArrayPool<TerrainLayerData>.Release(layerData);
            });
        }

        // Build id map
        var idMapResolution = terrainData.alphamapResolution;
        idMap = renderGraph.GetTexture(idMapResolution, idMapResolution, GraphicsFormat.R8_UInt, isPersistent: true);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Terrain Layer Data Init"))
        {
            pass.Initialize(generateIdMapMaterial);

            pass.WriteTexture(idMap, RenderBufferLoadAction.DontCare);

            //pass.ReadBuffer("TerrainLayerData", layerDataBuffer);

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                pass.SetInt(command, "LayerCount", terrainData.alphamapLayers);
                pass.SetFloat(command, "_Resolution", idMapResolution);
                (pass as FullscreenRenderPass).propertyBlock.SetBuffer("TerrainLayerData", terrainLayerData);

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
    }

    public void Cull(Vector3 viewPosition, Vector4[] cullingPlanes, ICommonPassData commonPassData)
    {
        if (terrain == null)
            return;

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

                    pass.SetVectorArray(command, "_CullingPlanes", cullingPlanes);

                    // Snap to quad-sized increments on largest cell
                    var position = terrain.GetPosition() - viewPosition;
                    var positionOffset = new Vector4(terrainData.size.x, terrainData.size.z, position.x, position.z);
                    pass.SetVector(command, "_TerrainPositionOffset", positionOffset);

                    pass.SetFloat(command, "_EdgeLength", (float)settings.EdgeLength * settings.PatchVertices);
                    pass.SetInt(command, "_CullingPlanesCount", cullingPlanes.Length);

                    pass.SetFloat(command, "_InputScale", terrainData.size.y);
                    pass.SetFloat(command, "_InputOffset", position.y);

                    pass.SetInt(command, "_MipCount", Texture2DExtensions.MipCount(terrainData.heightmapResolution) - 1);

                    ArrayPool<Vector4>.Release(cullingPlanes);
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

        renderGraph.ResourceMap.SetRenderPassData(new TerrainRenderCullResult(indirectArgsBuffer, patchDataBuffer));
    }

    public void Render(string passName, Vector3 viewPosititon, RTHandle cameraDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, Vector4[] cullingPlanes, ICommonPassData commonPassData)
    {
        if (terrain == null)
            return;

        var material = terrain.materialTemplate;
        Assert.IsNotNull(material, "Terrain Material is null");

        var passIndex = material.FindPass(passName);
        Assert.IsFalse(passIndex == -1, "Terrain Material has no Terrain Pass");

        var size = terrainData.size;
        var position = terrain.GetPosition() - viewPosititon;
        var passData = renderGraph.ResourceMap.GetRenderPassData<TerrainRenderCullResult>();

        using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Terrain Render"))
        {
            pass.WriteDepth(cameraDepth, RenderTargetFlags.None, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(albedoMetallic, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(normalRoughness, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(bentNormalOcclusion, RenderBufferLoadAction.DontCare);

            pass.Initialize(material, indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);
            pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);
            pass.ReadTexture("_TerrainHeightmapTexture", heightmap);
            pass.ReadTexture("_TerrainNormalMap", normalmap);

            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
            commonPassData.SetInputs(pass);

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);

                pass.SetTexture(command, "_TerrainHolesTexture", terrainData.holesTexture);

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

                // These may be needed in other passes
                pass.SetFloat(command, "_TerrainHeightScale", size.y);
                pass.SetFloat(command, "_TerrainHeightOffset", position.y);

                pass.SetVector(command, "_TerrainScaleOffset", new Vector4(1f / size.x, 1f / size.z, -position.x / size.x, -position.z / size.z));

                var terrainRemapHalfTexel = GraphicsUtilities.HalfTexelRemap(position.XZ(), size.XZ(), Vector2.one * terrainData.heightmapResolution);
                pass.SetVector(command, "_TerrainRemapHalfTexel", terrainRemapHalfTexel);

                pass.SetInt(command, "_CullingPlanesCount", 6);
                pass.SetVectorArray(command, "_CullingPlanes", cullingPlanes);

                pass.SetTexture(command, "AlbedoSmoothness", diffuseArray);
                pass.SetTexture(command, "Normal", normalMapArray);
                pass.SetTexture(command, "Mask", maskMapArray);

                (pass as DrawProceduralIndirectRenderPass).propertyBlock.SetBuffer("TerrainLayerData", terrainLayerData);
                pass.SetTexture(command, "IdMap", idMap);
                pass.SetFloat(command, "IdMapResolution", terrainData.alphamapResolution);
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

    public void SetInputs(RenderPass pass)
    {
        pass.ReadBuffer("_PatchData", PatchDataBuffer);
    }

    public void SetProperties(RenderPass pass, CommandBuffer command)
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
