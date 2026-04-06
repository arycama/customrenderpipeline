using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using static Math;
using UnityEngine.Pool;
using Unity.Collections;

public class VirtualTerrain : FrameRenderFeature
{
    private readonly TerrainSettings settings;
    private readonly ResourceHandle<GraphicsBuffer> feedbackBuffer, mappedTiles;
    private readonly ResourceHandle<RenderTexture> pageTable, pageTableMap;
    private readonly Texture2DArray albedoSmoothnessTexture, normalTexture, heightTexture;
    private readonly ComputeShader virtualTextureUpdate, dxtCompress;
    private readonly LruCache<int, int> lruCache = new();
    private readonly NativeList<int> pendingRequests = new(Allocator.Persistent);
    private readonly HashSet<int> queuedRequests = new();
    private readonly Material virtualTextureBuildMaterial;

    private Terrain previousTerrain;

    private bool[][,] pageTableResidency;

    private bool needsClear;
    private int previousAniso;

    private int PageTableSize => settings.VirtualResolution / settings.TileResolution;
    private int PageTableMipCount => Texture2DExtensions.MipCount(PageTableSize);
    private int Padding => Max(1, settings.AnisoLevel >> 1);

    public VirtualTerrain(RenderGraph renderGraph, TerrainSettings settings) : base(renderGraph)
    {
        this.settings = settings;

        virtualTextureUpdate = Resources.Load<ComputeShader>("Terrain/VirtualTextureUpdate");
        feedbackBuffer = renderGraph.GetBuffer(Texture2DExtensions.PixelCount(PageTableSize), isPersistent: true);

        // Mapped tiles contains an array of packed coords for each tile. Eg for each tile index it says the original X Y Z coorsd.
        mappedTiles = renderGraph.GetBuffer(settings.VirtualTileCount, sizeof(int), isPersistent: true);
        pageTableResidency = new bool[PageTableMipCount][,];
        for (var i = 0; i < PageTableMipCount; i++)
        {
            var mipSize = PageTableSize >> i;
            pageTableResidency[i] = new bool[mipSize, mipSize];
        }

        pageTableMap = renderGraph.GetTexture(PageTableSize, GraphicsFormat.R8_UNorm, hasMips: true, isRandomWrite: true, isPersistent: true);
        pageTable = renderGraph.GetTexture(PageTableSize, GraphicsFormat.R16_UInt, hasMips: true, isRandomWrite: true, isPersistent: true);

        virtualTextureBuildMaterial = new Material(Shader.Find("Hidden/Virtual Texture Build")) { hideFlags = HideFlags.HideAndDontSave };
        dxtCompress = Resources.Load<ComputeShader>("Terrain/DxtCompress");

        var resolution = settings.TileResolution;
        albedoSmoothnessTexture = new(resolution, resolution, settings.VirtualTileCount, GraphicsFormat.RGBA_DXT5_SRGB, TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate, 2)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "Virtual AlbedoSmoothness Texture"
        };

        normalTexture = new(resolution, resolution, settings.VirtualTileCount, GraphicsFormat.RGBA_DXT5_UNorm, TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate, 2)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "Virtual Normal Texture"
        };

        heightTexture = new(resolution, resolution, settings.VirtualTileCount, GraphicsFormat.R_BC4_UNorm, TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate, 2)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "Virtual Height Texture"
        };
    }

    protected override void Cleanup(bool disposing)
    {
        renderGraph.ReleasePersistentResource(pageTableMap, -1);
        renderGraph.ReleasePersistentResource(mappedTiles, -1);
        renderGraph.ReleasePersistentResource(feedbackBuffer, -1);
        renderGraph.ReleasePersistentResource(pageTable, -1);

        pendingRequests.Dispose();

        Object.DestroyImmediate(albedoSmoothnessTexture);
        Object.DestroyImmediate(normalTexture);
        Object.DestroyImmediate(heightTexture);
    }

    public override void Render(ScriptableRenderContext context)
    {
        var tileRect = new Rect(Padding, Padding, settings.TileResolution - 2 * Padding, settings.TileResolution - 2 * Padding);
        var uvScaleOffset = GraphicsUtilities.TexelRemapNormalized(tileRect, settings.TileResolution);
      //  uvScaleOffset.xy *= PageTableSize;

        var virtualTextureDataBuffer = renderGraph.SetConstantBuffer
        ((
            uvScaleOffset,
            (float)settings.VirtualResolution,
            (float)settings.TileResolution,
            (float)settings.AnisoLevel,
            (float)Texture2DExtensions.MipCount(PageTableSize),
            PageTableSize
        ));

        renderGraph.SetResource<VirtualTextureData>(new(albedoSmoothnessTexture, normalTexture, heightTexture, pageTable, feedbackBuffer, virtualTextureDataBuffer));

        // Clear if terrain has changed
        if (!renderGraph.TryGetResource<TerrainSystemData>(out var terrainSystemData))
            return;

        var terrain = terrainSystemData.terrain;
        needsClear |= terrain != previousTerrain;
        previousTerrain = terrain;

        needsClear |= previousAniso != settings.AnisoLevel;
        previousAniso = settings.AnisoLevel;

        // If terrain is different, clear the LRU cache
        if (needsClear)
        {
            pageTableResidency = new bool[PageTableMipCount][,];
            for (var i = 0; i < PageTableMipCount; i++)
            {
                var mipSize = PageTableSize >> i;
                pageTableResidency[i] = new bool[mipSize, mipSize];
            }

            lruCache.Clear();

            using (var pass = renderGraph.AddComputeRenderPass("Clear Buffer"))
            {
                pass.Initialize(virtualTextureUpdate, 3, settings.VirtualTileCount);
                pass.WriteBuffer("MappedTiles", mappedTiles);
            }

            // TODO: Can we not just use a hardware clear?
            for (var i = 0; i < PageTableMipCount; i++)
            {
                using var pass = renderGraph.AddComputeRenderPass("Clear Texture");
                var mipSize = Texture2DExtensions.MipResolution(i, PageTableSize);
                pass.Initialize(virtualTextureUpdate, Padding, mipSize, mipSize);
                pass.WriteTexture("DestMip", pageTableMap);
            }

            var pageTableMipCount = Texture2DExtensions.MipCount(PageTableSize);
            for (var i = 0; i < pageTableMipCount; i++)
            {
                var mipSize = Texture2DExtensions.MipResolution(i, PageTableSize);
                using var pass = renderGraph.AddComputeRenderPass("Clear Texture");
                pass.Initialize(virtualTextureUpdate, Padding, mipSize, mipSize);
                pass.WriteTexture("DestMip", pageTable);
            }

            needsClear = false;
        }

        queuedRequests.Clear();
        foreach (var packedPosition in pendingRequests)
        {
            var position = UnpackCoord(packedPosition);

            // If texture already mapped, nothing to do. (TODO: Can we detect this on GPU somehow to avoid adding to array? Probably not since we still should update the LRU cache)
            if (pageTableResidency[position.z][position.y, position.x])
            {
                // Mark this pixel as currently visible so it doesn't get evicted
                lruCache.Update(packedPosition);
                continue;
            }

            // We want to request the coarsest mip that is not yet rendered, to ensure there is a gradual transition to the
            // target mip, with 1 mip changing per frame. Do this by starting from current mip, and working to coarsest
            var iterations = PageTableMipCount - position.z;
            for (var j = 1; j < iterations; j++)
            {
                var newPosition = new Int3(position.x >> 1, position.y >> 1, position.z + 1);
                var isMapped = pageTableResidency[newPosition.z][newPosition.y, newPosition.x];
                if (isMapped)
                {
                    // If we found a higher fallback, add the next finer tile to the queue (Which will be the previous iteration tile)
                    _ = queuedRequests.Add(PackCoord(position));
                    break;
                }

                if (j == iterations - 1)
                {
                    // Most coarse mip, add
                    _ = queuedRequests.Add(PackCoord(newPosition));
                }

                position = newPosition;
            }
        }

        pendingRequests.Clear();

        // Process pending request, if any
        if (queuedRequests.Count == 0)
            return;

        var scaleOffsets = ListPool<Float4>.Get();
        var dstIndices = ListPool<int>.Get();
        var destPixels = ListPool<int>.Get();
        var tileCount = 0;

        // Sort requests by mip, then distance from camera
        var sortedRequests = queuedRequests.OrderByDescending(packedCoord => UnpackCoord(packedCoord).z);
        foreach (var packedPosition in sortedRequests)
        {
            int dstIndex;
            if (lruCache.Count < settings.VirtualTileCount)
            {
                // If we haven't exceeded our max tile count, use the next slot
                dstIndex = lruCache.Count;
            }
            else
            {
                // Otherwise get the slot of the least recently used tile
                var lastTileUsed = lruCache.Remove();
                dstIndex = lastTileUsed.Item2;

                // Invalidate existing position
                var previousCoord = UnpackCoord(lastTileUsed.Item1);
                pageTableResidency[previousCoord.z][previousCoord.y, previousCoord.x] = false;
            }

            // Add new tile to cache
            lruCache.Add(packedPosition, dstIndex);

            // Mark this pixel as filled in the array
            var position = UnpackCoord(packedPosition);
            pageTableResidency[position.z][position.y, position.x] = true;

            // Set some data for the ComputeShader to update the page table
            dstIndices.Add(dstIndex);
            destPixels.Add(packedPosition);

            var dstX = (position.x << position.z) * settings.TileResolution - (Padding << position.z);
            var dstY = (position.y << position.z) * settings.TileResolution - (Padding << position.z);
            var dstWidth = (settings.TileResolution + 2 * Padding) << position.z;
            var dstHeight = (settings.TileResolution + 2 * Padding) << position.z;

            var dstRect = new Rect(dstX, dstY, dstWidth, dstHeight);
            scaleOffsets.Add(GraphicsUtilities.TexelRemapNormalized(dstRect, settings.VirtualResolution));

            // Exit if we've reached the max number of tiles for this frame
            if (++tileCount == settings.MaxUpdatesPerFrame)
                break;
        }

        queuedRequests.Clear();

        // Copy Tiles to Unmap
        var destIndicesBuffer = renderGraph.GetBuffer(tileCount);
        var tilesToUnmapBuffer = renderGraph.GetBuffer(tileCount);
        using (var pass = renderGraph.AddComputeRenderPass("Copy Tiles To Unmap", (tileCount, destIndicesBuffer, dstIndices)))
        {
            pass.Initialize(virtualTextureUpdate, 0, tileCount);
            pass.WriteBuffer("TilesToUnmap", tilesToUnmapBuffer);
            pass.WriteBuffer("", destIndicesBuffer);

            pass.ReadBuffer("DestIndices", destIndicesBuffer);
            pass.ReadBuffer("MappedTiles", mappedTiles);

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                command.SetBufferData(pass.GetBuffer(data.destIndicesBuffer), data.dstIndices);
                pass.SetInt("MaxIndex", data.tileCount);
            });
        }

        // dispatch mip updates
        var destPixelbuffer = renderGraph.GetBuffer(tileCount);
        using (var pass = renderGraph.AddGenericRenderPass("Copy Dst Pixels", (destPixels, destPixelbuffer)))
        {
            pass.SetRenderFunction((command, pass, data) =>
            {
                command.SetBufferData(pass.GetBuffer(data.destPixelbuffer), data.destPixels);
                ListPool<int>.Release(data.destPixels);
            });
        }

        // Map new data
        var virtualTextureData = renderGraph.GetResource<VirtualTextureData>();
        for (var i = 0; i < PageTableMipCount; i++)
        {
            using (var pass = renderGraph.AddComputeRenderPass("Map New Data", (i, tileCount, destPixelbuffer)))
            {
                pass.Initialize(virtualTextureUpdate, 1, tileCount);
                pass.WriteTexture("PageTableWrite", virtualTextureData.pageTable, i);
                pass.WriteTexture("PageTableMap", pageTableMap, i);
                pass.WriteBuffer("MappedTiles", mappedTiles);
                pass.WriteBuffer("", destPixelbuffer);

                pass.ReadTexture("PageTableMap", pageTableMap, i);
                pass.ReadBuffer("TilesToUnmap", tilesToUnmapBuffer);
                pass.ReadBuffer("DestIndices", destIndicesBuffer);
                pass.ReadBuffer("DestPixels", destPixelbuffer);

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    pass.SetInt("CurrentMip", data.i);
                    pass.SetInt("MaxIndex", data.tileCount);
                });
            }
        }

        // Virtual Texture Update
        for (var i = PageTableMipCount - 2; i >= 0; i--)
        {
            var mipSize = PageTableSize >> i;
            using (var pass = renderGraph.AddComputeRenderPass("Page Table Update", mipSize))
            {
                pass.Initialize(virtualTextureUpdate, 2, mipSize, mipSize);
                pass.WriteTexture("DestMip", virtualTextureData.pageTable, i);
                pass.ReadTexture("SourceMip", virtualTextureData.pageTable, i + 1);
                pass.ReadTexture("PageTableMap", pageTableMap, i);

                pass.SetRenderFunction(static (command, pass, mipSize) =>
                {
                    pass.SetInt("MaxIndex", mipSize);
                });
            }
        }

        var virtualAlbedoTemp = renderGraph.GetTexture(settings.TileResolution, GraphicsFormat.R8G8B8A8_SRGB, settings.MaxUpdatesPerFrame, TextureDimension.Tex2DArray, isExactSize: true);
        var virtualNormalTemp = renderGraph.GetTexture(settings.TileResolution, GraphicsFormat.R8G8B8A8_UNorm, settings.MaxUpdatesPerFrame, TextureDimension.Tex2DArray, isExactSize: true);
        var virtualHeightTemp = renderGraph.GetTexture(settings.TileResolution, GraphicsFormat.R8_UNorm, settings.MaxUpdatesPerFrame, TextureDimension.Tex2DArray, isExactSize: true);
        var scaleOffsetsBuffer = renderGraph.GetBuffer(tileCount, sizeof(float) * 4);

        // Build the virtual texture
        using (var pass = renderGraph.AddGenericRenderPass("Build", (scaleOffsetsBuffer, scaleOffsets, settings.TileResolution, virtualAlbedoTemp, virtualNormalTemp, virtualHeightTemp, virtualTextureBuildMaterial, tileCount)))
        {
            pass.ReadResource<TerrainFrameData>();

            pass.WriteTexture(virtualAlbedoTemp);
            pass.WriteTexture(virtualNormalTemp);
            pass.WriteTexture(virtualHeightTemp);

            pass.WriteBuffer("", scaleOffsetsBuffer);
            pass.ReadBuffer("ScaleOffsets", scaleOffsetsBuffer);

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                // Upload the new positions
                command.SetBufferData(pass.GetBuffer(data.scaleOffsetsBuffer), data.scaleOffsets);

                var targets = ArrayPool<RenderTargetIdentifier>.Get(3);
                targets[0] = pass.GetRenderTexture(data.virtualAlbedoTemp);
                targets[1] = pass.GetRenderTexture(data.virtualNormalTemp);
                targets[2] = pass.GetRenderTexture(data.virtualHeightTemp);

                command.SetRenderTarget(targets, targets[0], 0, CubemapFace.Unknown, -1);
                command.DrawProcedural(Matrix4x4.identity, data.virtualTextureBuildMaterial, 0, MeshTopology.Triangles, 3 * data.tileCount, 1, pass.PropertyBlock);

                ListPool<Float4>.Release(data.scaleOffsets);
                ArrayPool<RenderTargetIdentifier>.Release(targets);
            });
        }

        var albedoCompressedTemp = renderGraph.GetTexture(settings.TileResolution >> 2, GraphicsFormat.R32G32B32A32_UInt, settings.MaxUpdatesPerFrame, TextureDimension.Tex2DArray, hasMips: true);
        var normalCompressedTemp = renderGraph.GetTexture(settings.TileResolution >> 2, GraphicsFormat.R32G32B32A32_UInt, settings.MaxUpdatesPerFrame, TextureDimension.Tex2DArray, hasMips: true);
        var heightCompressedTemp = renderGraph.GetTexture(settings.TileResolution >> 2, GraphicsFormat.R32G32_UInt, settings.MaxUpdatesPerFrame, TextureDimension.Tex2DArray, hasMips: true);

        using (var pass = renderGraph.AddComputeRenderPass("Compress"))
        {
            pass.Initialize(dxtCompress, 0, settings.TileResolution >> 2, settings.TileResolution >> 2, tileCount);

            pass.WriteTexture("AlbedoResult0", albedoCompressedTemp, 0);
            pass.WriteTexture("AlbedoResult1", albedoCompressedTemp, 1);

            pass.WriteTexture("NormalResult0", normalCompressedTemp, 0);
            pass.WriteTexture("NormalResult1", normalCompressedTemp, 1);

            pass.WriteTexture("HeightResult0", heightCompressedTemp, 0);
            pass.WriteTexture("HeightResult1", heightCompressedTemp, 1);

            pass.ReadTexture("AlbedoInput", virtualAlbedoTemp);
            pass.ReadTexture("NormalInput", virtualNormalTemp);
            pass.ReadTexture("HeightInput", virtualHeightTemp);
        }

        using (var pass = renderGraph.AddGenericRenderPass("Copy", new VirtualCopyPassData(dstIndices, settings.TileResolution, albedoCompressedTemp, normalCompressedTemp, heightCompressedTemp, virtualTextureData.albedoSmoothness, virtualTextureData.normal, virtualTextureData.height)))
        {
            pass.ReadTexture("", albedoCompressedTemp);
            pass.ReadTexture("", normalCompressedTemp);
            pass.ReadTexture("", heightCompressedTemp);

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                for (var i = 0; i < data.targetIndices.Count; i++)
                {
                    var dstOffset = data.targetIndices[i];

                    for (var j = 0; j < 2; j++)
                    {
                        var mipResolution = data.TileResolution >> j;
                        command.CopyTexture(pass.GetRenderTexture(data.albedoCompressId), i, j, 0, 0, mipResolution >> 2, mipResolution >> 2, data.albedoSmoothnessTexture, dstOffset, j, 0, 0);
                        command.CopyTexture(pass.GetRenderTexture(data.normalCompressId), i, j, 0, 0, mipResolution >> 2, mipResolution >> 2, data.normalTexture, dstOffset, j, 0, 0);
                        command.CopyTexture(pass.GetRenderTexture(data.heightCompressId), i, j, 0, 0, mipResolution >> 2, mipResolution >> 2, data.heightTexture, dstOffset, j, 0, 0);
                    }
                }

                ListPool<int>.Release(data.targetIndices);
            });
        }
    }

    internal void ReadbackRequestComplete(AsyncGPUReadbackRequest request)
    {
        // TODO: Does supplying our own native array reduce allocations or anything
        if (request.hasError)
            return;

        var requestData = request.GetData<int>();

        // First element is the number of elements
        var count = requestData[0] - 1;
        pendingRequests.AddRange(requestData.GetSubArray(1, count));
    }

    public static void OnTerrainTextureChanged(Terrain terrain, RectInt texelRegion)
    {
#if false
		foreach (var node in activeNodes)
		{
			var start = Vector2Int.FloorToInt((Vector2)texelRegion.min / terrain.terrainData.alphamapResolution * node.IndirectionTextureResolution);
			var end = Vector2Int.CeilToInt((Vector2)texelRegion.max / terrain.terrainData.alphamapResolution * node.IndirectionTextureResolution);

			// TODO: We also need to clear these pixels from the GPU copy. However, that would require filling a big buffer with pixels to clear, so just clear all for now
			node.needsClear = true;

			for (var mip = 0; mip < node.indirectionTexture.mipmapCount; mip++)
			{
				var mipStart = Vector2Int.FloorToInt((Vector2)start / Mathf.Pow(2, mip));
				var mipEnd = Vector2Int.CeilToInt((Vector2)end / Mathf.Pow(2, mip));

				// Offset in bytes for this mip in the array
				var targetOffset = Texture2DExtensions.MipOffset(mip, node.IndirectionTextureResolution);
				var mipSize = node.IndirectionTextureResolution / (int)Mathf.Pow(2, mip);

				var width = mipEnd.x - mipStart.x;
				var height = mipEnd.y - mipStart.y;

				// Set all the cells to false, this will make them get requested again next update
				for (var y = mipStart.y; y < mipStart.y + height; y++)
				{
					for (var x = mipStart.x; x < mipStart.x + width; x++)
					{
						var coord = x + y * mipSize;
						var target = targetOffset + coord;
						node.indirectionTexturePixels[target] = false;
					}
				}
			}
		}
#endif
    }

    private static int PackCoord(Int3 coord)
    {
        var data = BitPack(coord.x, 14, 0);
        data |= BitPack(coord.y, 14, 14);
        return data | BitPack(coord.z, 4, 28);
    }

    private static Int3 UnpackCoord(int packedCoord)
    {
        var x = BitUnpack(packedCoord, 14, 0);
        var y = BitUnpack(packedCoord, 14, 14);
        var mip = BitUnpack(packedCoord, 4, 28);
        return new(x, y, mip);
    }

    private struct VirtualCopyPassData
    {
        public List<int> targetIndices;
        public int TileResolution;
        public ResourceHandle<RenderTexture> albedoCompressId;
        public ResourceHandle<RenderTexture> normalCompressId;
        public ResourceHandle<RenderTexture> heightCompressId;
        public Texture2DArray albedoSmoothnessTexture;
        public Texture2DArray normalTexture;
        public Texture2DArray heightTexture;

        public VirtualCopyPassData(List<int> targetIndices, int tileResolution, ResourceHandle<RenderTexture> albedoCompressId, ResourceHandle<RenderTexture> normalCompressId, ResourceHandle<RenderTexture> heightCompressId, Texture2DArray albedoSmoothnessTexture, Texture2DArray normalTexture, Texture2DArray heightTexture)
        {
            this.targetIndices = targetIndices;
            TileResolution = tileResolution;
            this.albedoCompressId = albedoCompressId;
            this.normalCompressId = normalCompressId;
            this.heightCompressId = heightCompressId;
            this.albedoSmoothnessTexture = albedoSmoothnessTexture;
            this.normalTexture = normalTexture;
            this.heightTexture = heightTexture;
        }
    }
}