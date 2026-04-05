using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using static Math;
using UnityEngine.Pool;

public class VirtualTerrain : FrameRenderFeature
{
    private readonly TerrainSettings settings;
    private readonly TerrainSystem terrainSystem;
    private readonly ResourceHandle<GraphicsBuffer> feedbackBuffer;
    private readonly Texture2DArray albedoSmoothnessTexture, normalTexture, heightTexture;
    private readonly ResourceHandle<RenderTexture> indirectionTexture;
    private readonly ComputeShader virtualTextureUpdateShader, dxtCompressCS;

    private readonly LruCache<int, int> lruCache = new();
    private readonly ResourceHandle<GraphicsBuffer> tilesToUnmapBuffer, mappedTiles;
    private readonly ResourceHandle<RenderTexture> indirectionTextureMapTexture;

    private readonly HashSet<int> pendingRequests = new();
    private readonly Material virtualTextureBuildMaterial;

    private Terrain previousTerrain;

    private bool[][,] pageTableResidency;

    private bool needsClear;

    private int PageTableSize => settings.VirtualResolution / settings.TileResolution;
    private int PageTableMipCount => Texture2DExtensions.MipCount(PageTableSize);

    public VirtualTerrain(RenderGraph renderGraph, TerrainSettings settings, TerrainSystem terrainSystem) : base(renderGraph)
    {
        this.settings = settings;
        this.terrainSystem = terrainSystem;

        virtualTextureUpdateShader = Resources.Load<ComputeShader>("Terrain/VirtualTextureUpdate");
        feedbackBuffer = renderGraph.GetBuffer(PageTableSize * PageTableSize * 4 / 3, isPersistent: true);

        mappedTiles = renderGraph.GetBuffer(settings.VirtualTileCount, sizeof(int), isPersistent: true);
        pageTableResidency = new bool[PageTableMipCount][,];
        for(var i = 0; i < PageTableMipCount; i++)
        {
            var mipSize = PageTableSize >> i;
            pageTableResidency[i] = new bool[mipSize, mipSize];
        }

        indirectionTextureMapTexture = renderGraph.GetTexture(PageTableSize, GraphicsFormat.R8_UNorm, hasMips: true, isRandomWrite: true, isPersistent: true);
        indirectionTexture = renderGraph.GetTexture(PageTableSize, GraphicsFormat.R16_UInt, hasMips: true, isRandomWrite: true, isPersistent: true);

        virtualTextureBuildMaterial = new Material(Shader.Find("Hidden/Virtual Texture Build")) { hideFlags = HideFlags.HideAndDontSave };
        dxtCompressCS = Resources.Load<ComputeShader>("Terrain/DxtCompress");

        var resolution = settings.TileResolution;
        albedoSmoothnessTexture = new(resolution, resolution, settings.VirtualTileCount, GraphicsFormat.RGBA_DXT5_SRGB, TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate, 2)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "Virtual AlbedoSmoothness Texture",
        };

        normalTexture = new(resolution, resolution, settings.VirtualTileCount, GraphicsFormat.RGBA_DXT5_UNorm, TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate, 2)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "Virtual Normal Texture",
        };

        heightTexture = new(resolution, resolution, settings.VirtualTileCount, GraphicsFormat.R_BC4_UNorm, TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate, 2)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "Virtual Height Texture",
        };
    }

    protected override void Cleanup(bool disposing)
    {
        renderGraph.ReleasePersistentResource(indirectionTextureMapTexture, -1);
        renderGraph.ReleasePersistentResource(mappedTiles, -1);
        renderGraph.ReleasePersistentResource(feedbackBuffer, -1);
        renderGraph.ReleasePersistentResource(indirectionTexture, -1);

        Object.DestroyImmediate(albedoSmoothnessTexture);
        Object.DestroyImmediate(normalTexture);
        Object.DestroyImmediate(heightTexture);
    }

    public override void Render(ScriptableRenderContext context)
    {
        renderGraph.SetResource<VirtualTextureData>(new(albedoSmoothnessTexture, normalTexture, heightTexture, indirectionTexture, feedbackBuffer, renderGraph.SetConstantBuffer
            (new VirtualTextureCbufferData(
                GraphicsUtilities.TexelRemapNormalized(new Rect(4, 4, settings.TileResolution - 8, settings.TileResolution - 8), settings.TileResolution),
                (float)settings.AnisoLevel,
                (float)PageTableSize,
                Rcp(PageTableSize),
                (float)settings.VirtualResolution,
                Log2(settings.TileResolution),
                (float)settings.TileResolution,
                PageTableSize,
                settings.VirtualResolution,
                settings.TileResolution
        ))));

        // Clear if terrain has changed
        if (!renderGraph.TryGetResource<TerrainSystemData>(out var terrainSystemData))
            return;

        var terrain = terrainSystemData.terrain;
        needsClear |= terrain != previousTerrain;
        previousTerrain = terrain;

        var terrainPosition = (Float3)terrainSystem.Terrain.GetPosition();
        var size = (Float3)terrain.terrainData.size;

        renderGraph.SetResource<TerrainRenderData>
        (
            new
            (
                terrainSystem.diffuseArray,
                terrainSystem.normalMapArray,
                terrainSystem.maskMapArray,
                terrainSystem.heightmap,
                terrainSystem.normalmap,
                terrainSystem.idMap,
                terrainSystem.TerrainData.holesTexture,
                terrainSystem.terrainLayerData,
                terrainSystem.aoMap,
                renderGraph.SetConstantBuffer
                (
                    new Pass0Data
                    (
                        size,
                        terrainSystem.TerrainData.alphamapResolution,
                        terrainSystem.Terrain.GetPosition(),
                        size.y,
                        GraphicsUtilities.HalfTexelRemap(terrainPosition.xz, size.xz, Vector2.one * terrainSystem.TerrainData.heightmapResolution),
                        new Float4(1f / size.x, 1f / size.z, -terrainPosition.x / size.x, -terrainPosition.z / size.z),
                        GraphicsUtilities.HalfTexelRemap(terrain.terrainData.heightmapResolution),
                        terrainPosition.y,
                        terrain.terrainData.heightmapResolution
                    )
                )
            )
        );

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
                pass.Initialize(virtualTextureUpdateShader, 3, settings.VirtualTileCount);
                pass.WriteBuffer("MappedTiles", mappedTiles);
            }

            // TODO: Can we not just use a hardware clear?
            for (var i = 0; i < PageTableMipCount; i++)
            {
                using var pass = renderGraph.AddComputeRenderPass("Clear Texture");
                var mipSize = Texture2DExtensions.MipResolution(i, PageTableSize);
                pass.Initialize(virtualTextureUpdateShader, 4, mipSize, mipSize);
                pass.WriteTexture("DestMip", indirectionTextureMapTexture);
            }

            var indirectionMipCount = Texture2DExtensions.MipCount(PageTableSize) - 1;
            for (var i = 0; i < indirectionMipCount; i++)
            {
                var mipSize = Texture2DExtensions.MipResolution(i, PageTableSize);
                using var pass = renderGraph.AddComputeRenderPass("Clear Texture");
                pass.Initialize(virtualTextureUpdateShader, 4, mipSize, mipSize);
                pass.WriteTexture("DestMip", indirectionTexture);
            }

            needsClear = false;
        }

        // Process pending request, if any
        if (pendingRequests.Count == 0)
            return;

        // First, figure out which unused tiles we can use
        var updateRect = new RectInt();
        var updateMip = -1;

        // TODO: List Pool
        var scaleOffsets = ListPool<Float4>.Get();
        var dstOffsets = ListPool<int>.Get();
        var destPixels = ListPool<int>.Get();
        var tileRequests = ListPool<int>.Get();
        var tileCount = 0;

        // Sort requests by mip, then distance from camera
        var sortedRequests = pendingRequests.OrderByDescending(packedCoord => UnpackCoord(packedCoord).z);
        foreach (var packedPosition in sortedRequests)
        {
            var position = UnpackCoord(packedPosition);

            int targetIndex;
            if (updateMip == -1)
            {
                updateMip = position.z;
                updateRect = new RectInt(position.x, position.y, 1, 1);
            }

            // Remove currently-existing VirtualTextureTile in this location
            var nextTileIndex = lruCache.Count;
            if (nextTileIndex < settings.VirtualTileCount)
            {
                targetIndex = nextTileIndex;
            }
            else
            {
                var lastTileUsed = lruCache.Remove();
                targetIndex = lastTileUsed.Item2;
                var lastPackedCoord = lastTileUsed.Item1;
                var existingPosition = UnpackCoord(lastPackedCoord);

                // Invalidate existing position
                pageTableResidency[existingPosition.z][existingPosition.x, existingPosition.y] = false;

                // Set the mip just before the one being removed as the minimum update, so that it can fall back to the tile before it.
                existingPosition.x >>= 1;
                existingPosition.y >>= 1;
                existingPosition.z += 1;

                if (existingPosition.z > updateMip)
                {
                    var delta = 1 << existingPosition.z - updateMip;
                    updateMip = existingPosition.z;

                    updateRect.SetMinMax(updateRect.min / delta, updateRect.max / delta);
                    updateRect = updateRect.Encapsulate(existingPosition.x, existingPosition.y);
                }
                else if (existingPosition.z == updateMip)
                {
                    updateRect = updateRect.Encapsulate(existingPosition.x, existingPosition.y);
                }
            }

            // Track the highest mip, as the update starts at the highest mip and works down
            // We only need to update mips higher than the one that has changed.
            if (position.z > updateMip)
            {
                var delta = 1 << position.z - updateMip;
                updateMip = position.z;

                updateRect.SetMinMax(updateRect.min / delta, updateRect.max / delta);
                updateRect = updateRect.Encapsulate(position.x, position.y);
            }
            else if (position.z == updateMip)
            {
                updateRect = updateRect.Encapsulate(position.x, position.y);
            }

            // Add new tile to cache
            lruCache.Add(packedPosition, targetIndex);

            // Mark this pixel as filled in the array
            pageTableResidency[position.z][position.x, position.y] = true;

            // Set some data for the ComputeShader to update the indirectiontexture
            tileRequests.Add((targetIndex & 0xFFFF) | ((position.z & 0xFFFF) << 16));
            destPixels.Add(position.x | (position.y << 16));
            dstOffsets.Add(targetIndex);

            var padding = 4;

            var dstX = (position.x << position.z) * settings.TileResolution - (padding << position.z);
            var dstY = (position.y << position.z) * settings.TileResolution - (padding << position.z);
            var dstWidth = (settings.TileResolution + 2 * padding) << position.z;
            var dstHeight = (settings.TileResolution + 2 * padding) << position.z;

            var dstRect = new Rect(dstX, dstY, dstWidth, dstHeight);
            scaleOffsets.Add(GraphicsUtilities.TexelRemapNormalized(dstRect, settings.VirtualResolution));

            // Exit if we've reached the max number of tiles for this frame
            if (++tileCount == settings.UpdateTileCount)
                break;
        }

        pendingRequests.Clear();

        // Update the page table
        var tileRequestsBuffer = renderGraph.GetBuffer(tileRequests.Count);
        using (var pass = renderGraph.AddComputeRenderPass("Copy Tiles To Unmap", (tileCount, tileRequestsBuffer, tileRequests)))
        {
            pass.Initialize(virtualTextureUpdateShader, 0, destPixels.Count);
            pass.WriteBuffer("TilesToUnmap", tilesToUnmapBuffer);
            pass.WriteBuffer("", tileRequestsBuffer);

            pass.ReadBuffer("TileRequests", tileRequestsBuffer);
            pass.ReadBuffer("MappedTiles", mappedTiles);

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                command.SetBufferData(pass.GetBuffer(data.tileRequestsBuffer), data.tileRequests);
                pass.SetInt("MaxIndex", data.tileCount);
                ListPool<int>.Release(data.tileRequests);
            });
        }

        // dispatch mip updates
        var destPixelbuffer = renderGraph.GetBuffer(destPixels.Count);
        using (var pass = renderGraph.AddGenericRenderPass("Copy Dst Pixels", (destPixels, destPixelbuffer)))
        {
            pass.SetRenderFunction((command, pass, data) =>
            {
                command.SetBufferData(pass.GetBuffer(data.destPixelbuffer), data.destPixels);
                ListPool<int>.Release(data.destPixels);
            });
        }

        // Only update required mips (And extents?)
        // Max(0) because highest mip might request it's parent mip too, this is easier (and fast enough)
        //var start = Math.Max(0, mipCount - updateMip);
        //for (var z = start; z < mipCount; z++)

        var virtualTextureData = renderGraph.GetResource<VirtualTextureData>();
        for (var z = 0; z < PageTableMipCount; z++)
        {
            using (var pass = renderGraph.AddComputeRenderPass("Map New Data", (z, tileCount, destPixelbuffer)))
            {
                pass.Initialize(virtualTextureUpdateShader, 1, tileCount);
                pass.WriteTexture("IndirectionWrite", virtualTextureData.indirectionTexture, z);
                pass.WriteTexture("IndirectionTextureMap", indirectionTextureMapTexture, z);
                pass.WriteBuffer("MappedTiles", mappedTiles);
                pass.WriteBuffer("", destPixelbuffer);

                pass.ReadTexture("IndirectionTextureMap", indirectionTextureMapTexture, z);
                pass.ReadBuffer("TilesToUnmap", tilesToUnmapBuffer);
                pass.ReadBuffer("TileRequests", tileRequestsBuffer);
                pass.ReadBuffer("DestPixels", destPixelbuffer);

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    pass.SetInt("CurrentMip", data.z);
                    pass.SetInt("MaxIndex", data.tileCount);
                });
            }
        }

        for (var z = PageTableMipCount - 2; z >= 0; z--)
        {
            var mipSize = PageTableSize >> z;
            using (var pass = renderGraph.AddComputeRenderPass("Page Table Update", mipSize))
            {
                pass.Initialize(virtualTextureUpdateShader, 2, mipSize, mipSize);
                pass.WriteTexture("DestMip", virtualTextureData.indirectionTexture, z);
                pass.ReadTexture("SourceMip", virtualTextureData.indirectionTexture, z + 1);
                pass.ReadTexture("IndirectionTextureMap", indirectionTextureMapTexture, z);

                pass.SetRenderFunction(static (command, pass, mipSize) =>
                {
                    pass.SetInt("MaxIndex", mipSize);
                });
            }
        }

        var virtualAlbedoTemp = renderGraph.GetTexture(settings.TileResolution, GraphicsFormat.R8G8B8A8_SRGB, settings.UpdateTileCount, TextureDimension.Tex2DArray, isExactSize: true);
        var virtualNormalTemp = renderGraph.GetTexture(settings.TileResolution, GraphicsFormat.R8G8B8A8_UNorm, settings.UpdateTileCount, TextureDimension.Tex2DArray, isExactSize: true);
        var virtualHeightTemp = renderGraph.GetTexture(settings.TileResolution, GraphicsFormat.R8_UNorm, settings.UpdateTileCount, TextureDimension.Tex2DArray, isExactSize: true);
        var scaleOffsetsBuffer = renderGraph.GetBuffer(scaleOffsets.Count, sizeof(float) * 4);

        // Build the virtual texture
        using (var pass = renderGraph.AddGenericRenderPass("Build", (scaleOffsetsBuffer, scaleOffsets, settings.TileResolution, virtualAlbedoTemp, virtualNormalTemp, virtualHeightTemp, virtualTextureBuildMaterial, tileCount)))
        {
            pass.ReadResource<TerrainRenderData>();

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
            });
        }

        var albedoCompressedTemp = renderGraph.GetTexture(settings.TileResolution >> 2, GraphicsFormat.R32G32B32A32_UInt, settings.UpdateTileCount, TextureDimension.Tex2DArray, hasMips: true);
        var normalCompressedTemp = renderGraph.GetTexture(settings.TileResolution >> 2, GraphicsFormat.R32G32B32A32_UInt, settings.UpdateTileCount, TextureDimension.Tex2DArray, hasMips: true);
        var heightCompressedTemp = renderGraph.GetTexture(settings.TileResolution >> 2, GraphicsFormat.R32G32_UInt, settings.UpdateTileCount, TextureDimension.Tex2DArray, hasMips: true);

        using (var pass = renderGraph.AddComputeRenderPass("Compress"))
        {
            pass.Initialize(dxtCompressCS, 0, settings.TileResolution >> 2, settings.TileResolution >> 2, tileCount);

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

        using (var pass = renderGraph.AddGenericRenderPass("Copy", new VirtualCopyPassData(dstOffsets, settings.TileResolution, albedoCompressedTemp, normalCompressedTemp, heightCompressedTemp, virtualTextureData.albedoSmoothness, virtualTextureData.normal, virtualTextureData.height)))
        {
            pass.ReadTexture("", albedoCompressedTemp);
            pass.ReadTexture("", normalCompressedTemp);
            pass.ReadTexture("", heightCompressedTemp);

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                for (var i = 0; i < data.dstOffsets.Count; i++)
                {
                    var dstOffset = data.dstOffsets[i];

                    for (var j = 0; j < 2; j++)
                    {
                        var mipResolution = data.TileResolution >> j;
                        command.CopyTexture(pass.GetRenderTexture(data.albedoCompressId), i, j, 0, 0, mipResolution >> 2, mipResolution >> 2, data.albedoSmoothnessTexture, dstOffset, j, 0, 0);
                        command.CopyTexture(pass.GetRenderTexture(data.normalCompressId), i, j, 0, 0, mipResolution >> 2, mipResolution >> 2, data.normalTexture, dstOffset, j, 0, 0);
                        command.CopyTexture(pass.GetRenderTexture(data.heightCompressId), i, j, 0, 0, mipResolution >> 2, mipResolution >> 2, data.heightTexture, dstOffset, j, 0, 0);
                    }
                }

                ListPool<int>.Release(data.dstOffsets);
            });
        }
    }

    internal void ReadbackRequestComplete(AsyncGPUReadbackRequest request)
    {
        pendingRequests.Clear();

        var requestData = request.GetData<int>();

        // First element is the number of elements
        var count = requestData[0];

        // For each tile request, attempt to queue it if not already cached, and not already pending
        for (var i = 0; i < count; i++)
        {
            var packedPosition = requestData[i + 1];
            var position = UnpackCoord(packedPosition);

            // If texture already mapped, nothing to do. (TODO: Can we detect this on GPU somehow to avoid adding to array? Probably not since we still should update the LRU cache)
            if (pageTableResidency[position.z][position.x, position.y])
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
                var isMapped = pageTableResidency[newPosition.z][newPosition.x, newPosition.y];
                if (isMapped)
                {
                    // If we found a higher fallback, add the next finer tile to the queue (Which will be the previous iteration tile)
                    _ = pendingRequests.Add(PackCoord(position));

                    break;
                }

                position = newPosition;

                if (j == iterations - 1)
                {
                    // Most coarse mip, add
                    _ = pendingRequests.Add(PackCoord(position));
                }
            }
        }
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
        static int BitPack(int data, int size, int offset)
        {
            return (data & ((1 << size) - 1)) << offset;
        }

        var data = BitPack(coord.x, 13, 0);
        data |= BitPack(coord.y, 13, 13);
        return data | BitPack(coord.z, 6, 26);
    }

    private static Int3 UnpackCoord(int packedCoord)
    {
        static int BitUnpack(int data, int size, int offset)
        {
            return (data >> offset) & ((1 << size) - 1);
        }

        var x = BitUnpack(packedCoord, 13, 0);
        var y = BitUnpack(packedCoord, 13, 13);
        var mip = BitUnpack(packedCoord, 6, 26);
        return new(x, y, mip);
    }

    private struct VirtualCopyPassData
    {
        public List<int> dstOffsets;
        public int TileResolution;
        public ResourceHandle<RenderTexture> albedoCompressId;
        public ResourceHandle<RenderTexture> normalCompressId;
        public ResourceHandle<RenderTexture> heightCompressId;
        public Texture2DArray albedoSmoothnessTexture;
        public Texture2DArray normalTexture;
        public Texture2DArray heightTexture;

        public VirtualCopyPassData(List<int> dstOffsets, int tileResolution, ResourceHandle<RenderTexture> albedoCompressId, ResourceHandle<RenderTexture> normalCompressId, ResourceHandle<RenderTexture> heightCompressId, Texture2DArray albedoSmoothnessTexture, Texture2DArray normalTexture, Texture2DArray heightTexture)
        {
            this.dstOffsets = dstOffsets;
            TileResolution = tileResolution;
            this.albedoCompressId = albedoCompressId;
            this.normalCompressId = normalCompressId;
            this.heightCompressId = heightCompressId;
            this.albedoSmoothnessTexture = albedoSmoothnessTexture;
            this.normalTexture = normalTexture;
            this.heightTexture = heightTexture;
        }
    }

    struct VirtualTextureCbufferData
    {
        public Float4 Item1;
        public float Item2;
        public float Item3;
        public float Item4;
        public float Item5;
        public float Item6;
        public float Item7;
        public int IndirectionTextureResolution;
        public int VirtualResolution;
        public int TileResolution;

        public VirtualTextureCbufferData(Float4 item1, float item2, float item3, float item4, float item5, float item6, float item7, int indirectionTextureResolution, int virtualResolution, int tileResolution)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
            IndirectionTextureResolution = indirectionTextureResolution;
            VirtualResolution = virtualResolution;
            TileResolution = tileResolution;
        }
    }

    private struct Pass0Data
    {
        public Float3 size;
        public float Item2;
        public Vector3 Item3;
        public float Item4;
        public Float4 Item5;
        public Float4 Item6;
        public Float2 Item7;
        public float Item8;
        public float Item9;

        public Pass0Data(Float3 size, float item2, Vector3 item3, float item4, Float4 item5, Float4 item6, Float2 item7, float item8, float item9)
        {
            this.size = size;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
            Item8 = item8;
            Item9 = item9;
        }
    }
}