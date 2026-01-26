using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class VirtualTerrain : ViewRenderFeature
{
	private readonly TerrainSettings settings;

	private List<GraphicsBuffer> requestBuffers = new();
	private readonly ResourceHandle<GraphicsBuffer> counterBuffer, /*requestBuffer,*/ tilesToUnmapBuffer, mappedTiles;

	private readonly ResourceHandle<RenderTexture> indirectionTextureMapTexture;

	// Flattened 2D array storing a bool for each mapped tile
	private readonly bool[,,] indirectionTexturePixels;

	// Need to track requests so we don't request the same page multiple times
	private readonly HashSet<int> pendingRequests = new();

	private bool needsClear;

	private readonly LruCache<int, int> lruCache = new();

	private int IndirectionSize => settings.VirtualResolution / settings.TileResolution;
	private int IndirectionMips => Texture2DExtensions.MipCount(IndirectionSize);

	private Terrain previousTerrain;

	private readonly ComputeShader virtualTextureUpdateShader, dxtCompressCS, virtualTextureBuild;

	private List<NativeArray<int>> requestArrays = new();
	private Stack<int> availableRequestIndices = new();
	private Queue<int> readyRequestIndices = new();

	private int maxRequestBufferSize;
	private readonly Material virtualTextureBuildMaterial;

	public VirtualTerrain(RenderGraph renderGraph, TerrainSettings settings) : base(renderGraph)
	{
		this.settings = settings;

		// Contains a simple 0 or 1 indicating if a pixel is mapped.
		indirectionTextureMapTexture = renderGraph.GetTexture(IndirectionSize, GraphicsFormat.R8_UNorm, hasMips: true, isRandomWrite: true, isPersistent: true);

		indirectionTexturePixels = new bool[IndirectionSize, IndirectionSize, IndirectionMips];

		counterBuffer = renderGraph.GetBuffer(1, 4, GraphicsBuffer.Target.Raw, isPersistent: true);

		// Buffer stuff
		mappedTiles = renderGraph.GetBuffer(settings.VirtualTileCount, sizeof(int), isPersistent: true);
		tilesToUnmapBuffer = renderGraph.GetBuffer(settings.UpdateTileCount, sizeof(int), isPersistent: true);

		virtualTextureBuild = Resources.Load<ComputeShader>("Terrain/VirtualTextureBuild");
		virtualTextureUpdateShader = Resources.Load<ComputeShader>("Terrain/VirtualTextureUpdate");
		dxtCompressCS = Resources.Load<ComputeShader>("Terrain/DxtCompress");

		virtualTextureBuildMaterial = new Material(Shader.Find("Hidden/Virtual Texture Build")) { hideFlags = HideFlags.HideAndDontSave };
	}

	protected override void Cleanup(bool disposing)
	{
		renderGraph.ReleasePersistentResource(indirectionTextureMapTexture, -1);
		renderGraph.ReleasePersistentResource(counterBuffer, -1);
		renderGraph.ReleasePersistentResource(mappedTiles, -1);
		renderGraph.ReleasePersistentResource(tilesToUnmapBuffer, -1);

		AsyncGPUReadback.WaitAllRequests();

        foreach(var buffer in requestBuffers)
        {
            buffer.Dispose();
        }

		foreach (var thing in requestArrays)
		{
			if(thing.IsCreated)
				thing.Dispose();
		}
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		// Ensure terrain system data is set
		if (!renderGraph.TryGetResource<TerrainSystemData>(out var terrainSystemData))
			return;

		var terrain = terrainSystemData.terrain;
		if (terrain == null)
			return;

		var terrainData = terrain.terrainData;

		using var scope = renderGraph.AddProfileScope("Virtual Terrain");

		// If terrain is different, clear the LRU cache
		if (terrain != previousTerrain || needsClear)
		{
			Array.Clear(indirectionTexturePixels, 0, indirectionTexturePixels.Length);
			lruCache.Clear();

			using (var pass = renderGraph.AddComputeRenderPass("Clear Buffer"))
			{
				pass.Initialize(virtualTextureUpdateShader, 3, settings.VirtualTileCount);
				pass.WriteBuffer("MappedTiles", mappedTiles);
			}

			// TODO: Can we not just use a hardware clear?
			for (var i = 0; i < IndirectionMips; i++)
			{
				using var pass = renderGraph.AddComputeRenderPass("Clear Texture");
				var mipSize = Texture2DExtensions.MipResolution(i, IndirectionSize);
				pass.Initialize(virtualTextureUpdateShader, 4, mipSize, mipSize);
				pass.WriteTexture("DestMip", indirectionTextureMapTexture);
			}

			needsClear = false;
		}

		previousTerrain = terrain;

		var virtualTextureData = renderGraph.GetResource<VirtualTextureData>();

		// Worst case scenario would be every pixel requesting a different patch+uv, though this is never going to happen in practice
		// Allocate the largest we need, plus one pixel since we include the 'count' as the first element
		maxRequestBufferSize = Math.Max(maxRequestBufferSize, viewRenderData.viewSize.x * viewRenderData.viewSize.y + 1);

        // Retrieve a stored array or fetch a new one
        NativeArray<int> requestArray;
        GraphicsBuffer requestBuffer;
        if (!availableRequestIndices.TryPop(out var requestArrayIndex))
        {
            requestArrayIndex = requestArrays.Count;
            requestArray = new NativeArray<int>(maxRequestBufferSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            requestArrays.Add(requestArray);

            requestBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, maxRequestBufferSize, 4);

            // Fill all buffesr with 0 (I think this should happen automatically, but
            using (var pass = renderGraph.AddGenericRenderPass("Virtual Texture Init", maxRequestBufferSize))
            {
                pass.SetRenderFunction((command, pass, requestBufferSize) =>
                {
                    command.SetBufferData(requestBuffer, new int[requestBufferSize]);
                    command.SetBufferCounterValue(requestBuffer, 1); // Init the counter to zero before appending
                });
            }
            requestBuffers.Add(requestBuffer);
        }
        else
        {
            requestArray = requestArrays[requestArrayIndex];

            if (requestArray.Length < maxRequestBufferSize)
            {
                requestArray.Dispose();
                requestArray = new NativeArray<int>(maxRequestBufferSize, Allocator.Persistent);
                requestArrays[requestArrayIndex] = requestArray;
            }

            requestBuffer = requestBuffers[requestArrayIndex];
            if (requestBuffer == null || !requestBuffer.IsValid() || requestBuffer.count < maxRequestBufferSize)
            {
                if (requestBuffer != null && requestBuffer.IsValid())
                    requestBuffer.Dispose();

                requestBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, maxRequestBufferSize, 4);
                requestBuffers[requestArrayIndex] = requestBuffer;

                // Fill all buffesr with 0 (I think this should happen automatically, but
                using (var pass = renderGraph.AddGenericRenderPass("Virtual Texture Init", maxRequestBufferSize))
                {
                    pass.SetRenderFunction((command, pass, requestBufferSize) =>
                    {
                        command.SetBufferData(requestBuffer, new int[requestBufferSize]);
                        command.SetBufferCounterValue(requestBuffer, 1); // Init the counter to zero before appending
                    });
                }
            }
        }

		using (var pass = renderGraph.AddComputeRenderPass("Gather Requested Pages", (requestBuffer, virtualTextureUpdateShader, IndirectionSize)))
		{
			var threadCount = IndirectionSize * IndirectionSize * 4 / 3;
			pass.Initialize(virtualTextureUpdateShader, 5, threadCount);
			pass.WriteBuffer("VirtualFeedbackTexture", virtualTextureData.feedbackBuffer);

			pass.SetRenderFunction((Action<CommandBuffer, RenderPass, (GraphicsBuffer requestBuffer, ComputeShader virtualTextureUpdateShader, int IndirectionTextureResolution)>)(static (command, pass, data) =>
			{
				command.ClearRandomWriteTargets(); // Clear from previous passes
				command.SetBufferCounterValue(data.requestBuffer, 1); // Init the counter to zero before appending
				command.SetComputeBufferParam(data.virtualTextureUpdateShader, 5, "VirtualRequests", data.requestBuffer);
				pass.SetInt("IndirectionResolution", data.IndirectionTextureResolution);
			}));
		}

		using (var pass = renderGraph.AddGenericRenderPass("Copy Counter and readback", (requestBuffer, requestArray, requestArrayIndex, readyRequestIndices)))
		{
			pass.SetRenderFunction(static (command, pass, data) =>
			{
				command.CopyCounterValue(data.requestBuffer, data.requestBuffer, 0);
                command.RequestAsyncReadbackIntoNativeArray(ref data.Item2, data.requestBuffer, (request) =>
                {
                    data.readyRequestIndices.Enqueue(data.requestArrayIndex);
                });
            });
		}

		while (readyRequestIndices.TryDequeue(out var readyRequestIndex))
		{
			var requestData = requestArrays[readyRequestIndex];

			// First element is the number of elements
			var count = requestData[0];

			// For each tile request, attempt to queue it if not already cached, and not already pending
			for (var i = 0; i < count; i++)
			{
				var packedPosition = requestData[i + 1];
				var position = UnpackCoord(packedPosition);

				// If texture already mapped, nothing to do. (TODO: Can we detect this on GPU somehow to avoid adding to array? Probably not since we still should update the LRU cache)
				if (indirectionTexturePixels[position.x, position.y, position.z])
				{
					// Mark this pixel as currently visible so it doesn't get evicted
					lruCache.Update(packedPosition);
					continue;
				}

				// We want to request the coarsest mip that is not yet rendered, to ensure there is a gradual transition to the
				// target mip, with 1 mip changing per frame. Do this by starting from current mip, and working to coarsest
				var iterations = IndirectionMips - position.z;
				for (var j = 1; j < iterations; j++)
				{
					var newPosition = new Int3(position.x >> 1, position.y >> 1, position.z + 1);
					var isMapped = indirectionTexturePixels[newPosition.x, newPosition.y, newPosition.z];
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

			availableRequestIndices.Push(readyRequestIndex);
		}

		if (pendingRequests.Count == 0)
			return;

		// Sort requests by mip, then distance from camera
		// TODO: Could do this on GPU before reading back.
		var sortedRequests = pendingRequests.OrderByDescending(packedCoord => UnpackCoord(packedCoord).z);

		// First, figure out which unused tiles we can use
		var updateRect = new RectInt();
		var updateMip = -1;

		// TODO: List Pool
		var scaleOffsets = new List<Vector4>();
		var dstOffsets = new List<int>();
		var destPixels = new List<int>();
		var tileRequests = new List<int>();

		var tileCount = 0;
		foreach (var packedPosition in pendingRequests.OrderByDescending(packedCoord => UnpackCoord(packedCoord).z))
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
				indirectionTexturePixels[existingPosition.x, existingPosition.y, existingPosition.z] = false;

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
			indirectionTexturePixels[position.x, position.y, position.z] = true;

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

		// Update the indirection texture
		var tileRequestsBuffer = renderGraph.GetBuffer(tileRequests.Count);
		using (var pass = renderGraph.AddComputeRenderPass("Copy Tiles To Unmap", (maxIndex: destPixels.Count, tileRequestsBuffer, tileRequests)))
		{
			pass.Initialize(virtualTextureUpdateShader, 0, destPixels.Count);
			pass.WriteBuffer("TilesToUnmap", tilesToUnmapBuffer);
			pass.WriteBuffer("", tileRequestsBuffer);

			pass.ReadBuffer("TileRequests", tileRequestsBuffer);
			pass.ReadBuffer("MappedTiles", mappedTiles);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				command.SetBufferData(pass.GetBuffer(data.tileRequestsBuffer), data.tileRequests);
				pass.SetInt("MaxIndex", data.maxIndex);
			});
		}

		// dispatch mip updates
		var destPixelbuffer = renderGraph.GetBuffer(destPixels.Count);

		// Only update required mips (And extents?)
		// Max(0) because highest mip might request it's parent mip too, this is easier (and fast enough)
		//var start = Math.Max(0, mipCount - updateMip);
		//for (var z = start; z < mipCount; z++)

		for (var z = 0; z < IndirectionMips; z++)
		{
			using (var pass = renderGraph.AddComputeRenderPass("Map New Data", (currentMip: z, maxIndex: tileRequests.Count, destPixelbuffer, destPixels)))
			{
				pass.Initialize(virtualTextureUpdateShader, 1, tileRequests.Count);
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
					command.SetBufferData(pass.GetBuffer(data.destPixelbuffer), data.destPixels);
					pass.SetInt("CurrentMip", data.currentMip);
					pass.SetInt("MaxIndex", data.maxIndex);
				});
			}
		}

		for (var z = IndirectionMips - 2; z >= 0; z--)
		{
			var mipSize = IndirectionSize >> z;
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

		// TODO: Should these be texture arrays instead?
		var updateTempWidth = settings.TileResolution;
		var virtualAlbedoTemp = renderGraph.GetTexture(settings.TileResolution, GraphicsFormat.R8G8B8A8_SRGB, settings.UpdateTileCount, TextureDimension.Tex2DArray, isExactSize: true);
		var virtualNormalTemp = renderGraph.GetTexture(settings.TileResolution, GraphicsFormat.R8G8B8A8_UNorm, settings.UpdateTileCount, TextureDimension.Tex2DArray, isExactSize: true);
		var virtualHeightTemp = renderGraph.GetTexture(settings.TileResolution, GraphicsFormat.R8_UNorm, settings.UpdateTileCount, TextureDimension.Tex2DArray, isExactSize: true);

		var scaleOffsetsBuffer = renderGraph.GetBuffer(scaleOffsets.Count, sizeof(float) * 4);

		// Build the virtual texture
		using (var pass = renderGraph.AddGenericRenderPass("Build", (scaleOffsetsBuffer, scaleOffsets, settings.TileResolution, virtualAlbedoTemp, virtualNormalTemp, virtualHeightTemp, virtualTextureBuildMaterial, tileCount)))
		{
			//pass.Initialize(virtualTextureBuildMaterial, 0, tileCount);

			pass.ReadResource<TerrainRenderData>();
			pass.ReadResource<ViewData>();

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
					// Build compute shader twice, once for base mip, and once for second mip, so we can use HW trilinear filtering
					for (var j = 0; j < 2; j++)
					{
						var mipResolution = data.TileResolution >> j;

						// Copy albedo, normal and height
						command.CopyTexture(pass.GetRenderTexture(data.albedoCompressId), i, j, 0, 0, mipResolution >> 2, mipResolution >> 2, data.albedoSmoothnessTexture, data.dstOffsets[i], j, 0, 0);
						command.CopyTexture(pass.GetRenderTexture(data.normalCompressId), i, j, 0, 0, mipResolution >> 2, mipResolution >> 2, data.normalTexture, data.dstOffsets[i], j, 0, 0);
						command.CopyTexture(pass.GetRenderTexture(data.heightCompressId), i, j, 0, 0, mipResolution >> 2, mipResolution >> 2, data.heightTexture, data.dstOffsets[i], j, 0, 0);
					}
				}
			});
		}
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
}
