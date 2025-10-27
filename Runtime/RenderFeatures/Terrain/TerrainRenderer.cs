using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class TerrainRenderer : TerrainRendererBase
{
	private readonly ResourceHandle<GraphicsBuffer> feedbackBuffer;

	private GraphicsBuffer requestBuffer;
	private ResourceHandle<GraphicsBuffer> counterBuffer, /*requestBuffer,*/ tilesToUnmapBuffer, mappedTiles;

	private ResourceHandle<RenderTexture> indirectionTexture, indirectionTextureMapTexture;
	private Texture2DArray albedoSmoothnessTexture, normalTexture, heightTexture;

	// Flattened 2D array storing a bool for each mapped tile
	private bool[] indirectionTexturePixels;

	// Need to track requests so we don't request the same page multiple times
	private readonly HashSet<int> pendingRequests = new();

	private bool needsClear;

	private readonly LruCache<int, int> lruCache = new();
	private int IndirectionTextureResolution => settings.VirtualResolution / settings.TileResolution;
	private Terrain previousTerrain;

	private readonly ComputeShader virtualTextureUpdateShader, dxtCompressCS, virtualTextureBuild, reductionComputeShader;

	public TerrainRenderer(RenderGraph renderGraph, TerrainSettings settings, QuadtreeCull quadtreeCull) : base(renderGraph, settings, quadtreeCull)
	{
		var indirectionTextureResolution = settings.VirtualResolution / settings.TileSize;

		// Request size is res * res * 1/3rd
		var requestSize = indirectionTextureResolution * indirectionTextureResolution * 4 / 3;
		feedbackBuffer = renderGraph.GetBuffer(requestSize, isPersistent: true);

		indirectionTexture = renderGraph.GetTexture(IndirectionTextureResolution, IndirectionTextureResolution, GraphicsFormat.R16_UInt, hasMips: true, isRandomWrite: true, isPersistent: true);

		// Contains a simple 0 or 1 indicating if a pixel is mapped.
		indirectionTextureMapTexture = renderGraph.GetTexture(IndirectionTextureResolution, IndirectionTextureResolution, GraphicsFormat.R8_UNorm, hasMips: true, isRandomWrite: true, isPersistent: true);

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

		indirectionTexturePixels = new bool[requestSize];

		//requestBuffer = renderGraph.GetBuffer(requestSize, 4, GraphicsBuffer.Target.Append, isPersistent: true);
		requestBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, requestSize, 4);
		counterBuffer = renderGraph.GetBuffer(1, 4, GraphicsBuffer.Target.Raw, isPersistent: true);

		// Fill all buffesr with 0 (I think this should happen automatically, but
		using (var pass = renderGraph.AddGenericRenderPass("Virtual Texture Init"))
		{
			pass.WriteBuffer("", feedbackBuffer);
			pass.WriteBuffer("", counterBuffer);

			pass.SetRenderFunction((command, pass) =>
			{
				command.SetBufferData(pass.GetBuffer(feedbackBuffer), new int[requestSize]);
				command.SetBufferData(requestBuffer, new int[requestSize]);
				command.SetBufferCounterValue(requestBuffer, 0u);
				command.SetBufferData(pass.GetBuffer(counterBuffer), new int[1]);
			});
		}

		// Buffer stuff
		mappedTiles = renderGraph.GetBuffer(settings.VirtualTileCount, sizeof(int), isPersistent: true);
		tilesToUnmapBuffer = renderGraph.GetBuffer(settings.UpdateTileCount, sizeof(int), isPersistent: true);

		reductionComputeShader = Resources.Load<ComputeShader>("Terrain/VirtualTerrain");
		virtualTextureBuild = Resources.Load<ComputeShader>("Terrain/VirtualTextureBuild");
		virtualTextureUpdateShader = Resources.Load<ComputeShader>("Terrain/VirtualTextureUpdate");
		dxtCompressCS = Resources.Load<ComputeShader>("Terrain/DxtCompress");
	}

	protected override void Cleanup(bool disposing)
	{
		renderGraph.ReleasePersistentResource(feedbackBuffer);
		renderGraph.ReleasePersistentResource(indirectionTexture);
		renderGraph.ReleasePersistentResource(indirectionTextureMapTexture);

		Object.DestroyImmediate(albedoSmoothnessTexture);
		Object.DestroyImmediate(normalTexture);
		Object.DestroyImmediate(heightTexture);

		requestBuffer.Dispose();
		renderGraph.ReleasePersistentResource(counterBuffer);
		renderGraph.ReleasePersistentResource(mappedTiles);
		renderGraph.ReleasePersistentResource(tilesToUnmapBuffer);
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		// Ensure terrain system data is set
		if (!renderGraph.TryGetResource<TerrainSystemData>(out var terrainSystemData))
			return;

		// Also ensure this is valid for current frame
		if (!renderGraph.TryGetResource<TerrainRenderData>(out var terrainRenderData))
			return;

		if (terrainSystemData.terrain == null || settings.Material == null)
			return;

		// Used by tessellation to calculate lod
		var size = terrainSystemData.terrainData.size;
		var indTexSize = new Vector4(1f / size.x, 1f / size.z, size.x, size.z);

		renderGraph.SetResource<VirtualTextureData>(new(albedoSmoothnessTexture, normalTexture, heightTexture, indirectionTexture, settings.AnisoLevel, settings.VirtualResolution, indTexSize));

		var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().cullingPlanes;
		var passData = Cull(camera.transform.position, cullingPlanes, camera.ViewSize());
		var passIndex = settings.Material.FindPass("Terrain");
		Assert.IsFalse(passIndex == -1, "Terrain Material has no Terrain Pass");

		using (var pass = renderGraph.AddDrawProceduralIndirectIndexedRenderPass("Terrain Render", (
			VerticesPerTileEdge,
			size,
			settings,
			position: terrainSystemData.terrain.GetPosition() - camera.transform.position,
			cullingPlanes,
			terrainSystemData.terrainData.heightmapResolution,
			feedbackBuffer,
			settings.VirtualResolution,
			settings.AnisoLevel
		)))
		{
			pass.Initialize(settings.Material, terrainSystemData.indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.None, RenderBufferLoadAction.DontCare);
			pass.WriteBuffer("_VirtualFeedbackTexture", feedbackBuffer);

			pass.ReadTexture("_IndirectionTexture", indirectionTexture);
			pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);

			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<TerrainRenderData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<VirtualTextureData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetInt("_VerticesPerEdge", data.VerticesPerTileEdge);
				pass.SetInt("_VerticesPerEdgeMinusOne", data.VerticesPerTileEdge - 1);
				pass.SetFloat("_RcpVerticesPerEdge", 1f / data.VerticesPerTileEdge);
				pass.SetFloat("_RcpVerticesPerEdgeMinusOne", 1f / (data.VerticesPerTileEdge - 1));

				var scaleOffset = new Vector4(data.size.x / data.settings.CellCount, data.size.z / data.settings.CellCount, data.position.x, data.position.z);
				pass.SetVector("_PatchScaleOffset", scaleOffset);
				pass.SetVector("_SpacingScale", new Vector4(data.size.x / data.settings.CellCount / data.settings.PatchVertices, data.size.z / data.settings.CellCount / data.settings.PatchVertices, data.position.x, data.position.z));
				pass.SetFloat("_PatchUvScale", 1f / data.settings.CellCount);

				pass.SetFloat("_HeightUvScale", 1f / data.settings.CellCount * (1.0f - 1f / data.heightmapResolution));
				pass.SetFloat("_HeightUvOffset", 0.5f / data.heightmapResolution);
				pass.SetFloat("_MaxLod", Math.Log2(data.settings.CellCount));

				pass.SetInt("_CullingPlanesCount", data.cullingPlanes.Count);
				var cullingPlanesArray = ArrayPool<Vector4>.Get(data.cullingPlanes.Count);
				for (var i = 0; i < data.cullingPlanes.Count; i++)
					cullingPlanesArray[i] = data.cullingPlanes.GetCullingPlaneVector4(i);

				pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
				ArrayPool<Vector4>.Release(cullingPlanesArray);

				pass.SetFloat("_VirtualUvScale", data.VirtualResolution);
				pass.SetFloat("_AnisoLevel", data.AnisoLevel);

				command.SetRandomWriteTarget(6, pass.GetBuffer(data.feedbackBuffer));
			});
		}

		using (var pass = renderGraph.AddObjectRenderPass("Render Terrain Replacement", feedbackBuffer))
		{
			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;
			pass.Initialize("Terrain", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.None);
			pass.WriteBuffer("_VirtualFeedbackTexture", feedbackBuffer);
			pass.AddRenderPassData<ViewData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				command.SetRandomWriteTarget(6, pass.GetBuffer(data));
			});
		}

		using var scope = renderGraph.AddProfileScope("Virtual Terrain");

		// If terrain is different, clear the LRU cache
		if (terrainSystemData.terrain != previousTerrain || needsClear)
		{
			Array.Clear(indirectionTexturePixels, 0, indirectionTexturePixels.Length);
			lruCache.Clear();

			using(var pass = renderGraph.AddComputeRenderPass("Clear Buffer"))
			{
				pass.Initialize(virtualTextureUpdateShader, 3, settings.VirtualTileCount);
				pass.WriteBuffer("MappedTiles", mappedTiles);
			}

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

				using (var pass = renderGraph.AddComputeRenderPass("Clear Texture"))
				{
					pass.Initialize(virtualTextureUpdateShader, 4, mipSize, mipSize);
					pass.WriteTexture("DestMip", indirectionTextureMapTexture);
				}
			}

			needsClear = false;
		}

		previousTerrain = terrainSystemData.terrain;

		using (var pass = renderGraph.AddComputeRenderPass("Gather Requested Pages", (requestBuffer, reductionComputeShader)))
		{
			var threadCount = IndirectionTextureResolution * IndirectionTextureResolution * 4 / 3;
			pass.Initialize(reductionComputeShader, 0, threadCount);

			pass.WriteBuffer("VirtualFeedbackTexture", feedbackBuffer);

			pass.AddRenderPassData<TerrainRenderData>();
			pass.AddRenderPassData<ViewData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				command.ClearRandomWriteTargets(); // Clear from previous passes
				command.SetBufferCounterValue(data.requestBuffer, 0); // Init the counter to zero before appending
				command.SetComputeBufferParam(data.reductionComputeShader, 0, "VirtualRequests", data.requestBuffer);
			});
		}

		using (var pass = renderGraph.AddGenericRenderPass("Copy and Readback Counter", (requestBuffer, counterBuffer, IndirectionTextureResolution, indirectionTexturePixels, lruCache, pendingRequests)))
		{
			pass.WriteBuffer("", counterBuffer);
			pass.ReadBuffer("", counterBuffer);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				// Copy the counter to a new buffer and read it back
				command.CopyCounterValue(data.requestBuffer, pass.GetBuffer(data.counterBuffer), 0);
				command.RequestAsyncReadback(pass.GetBuffer(data.counterBuffer), (request) =>
				{
					var size = request.GetData<uint>()[0] * 4;
					if (size == 0u)
						return;

					_ = AsyncGPUReadback.Request(data.requestBuffer, (int)size, 0, (readbackRequest) =>
					{
						// For each tile request, attempt to queue it if not already cached, and not already pending
						if (readbackRequest.hasError)
						{
							return;
						}

						var mipCount = (int)Math.Log2(data.IndirectionTextureResolution);
						var readbackData = readbackRequest.GetData<int>();
						foreach (var request in readbackData)
						{
							var position = Texture2DExtensions.TextureByteOffsetToCoord(request, data.IndirectionTextureResolution);

							// We want to request the coarsest mip that is not yet rendered, to ensure there is a gradual transition to the
							// target mip, with 1 mip changing per frame. Do this by starting from current mip, and working to coarsest
							var previousIndex = request;
							for (var i = position.z; i <= mipCount; i++)
							{
								var index = Texture2DExtensions.TextureCoordToOffset(new Vector3Int(position.x, position.y, i), data.IndirectionTextureResolution);

								var indirectionTexturePixel = data.indirectionTexturePixels[index];
								if (indirectionTexturePixel)
								{
									data.lruCache.Update(index);

									// If this is not the targetMip, add the next coarsest mip
									if (index != request)
									{
										_ = data.pendingRequests.Add(previousIndex);
									}

									// Found a fallback mip, break
									break;
								}
								else if (i == mipCount)
								{
									// Most coarse mip, add
									_ = data.pendingRequests.Add(index);
								}
								else
								{
									previousIndex = index;

									position.x >>= 1;
									position.y >>= 1;
								}
							}
						}
					});
				});
			});
		}

		if (pendingRequests.Count < 1)
			return;

		// Sort requests by mip, then distance from camera
		// TODO: Could do this on GPU before reading back.
		var sortedRequests = pendingRequests.OrderByDescending(rq => Texture2DExtensions.TextureByteOffsetToCoord(rq, IndirectionTextureResolution).z);

		// First, figure out which unused tiles we can use
		var updateRect = new RectInt();
		var updateMip = -1;

		// TODO: List Pool
		var scaleOffsets = new List<Vector4>();
		var dstOffsets = new List<uint>();
		var destPixels = new List<uint>();
		var tileRequests = new List<uint>();

		var index = 0;
		foreach (var request in sortedRequests)
		{
			if (lruCache.Contains(request))
				continue;

			var position = Texture2DExtensions.TextureByteOffsetToCoord(request, IndirectionTextureResolution);

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
				var lastIndex = lastTileUsed.Item1;
				var existingPosition = Texture2DExtensions.TextureByteOffsetToCoord(lastIndex, IndirectionTextureResolution);

				// Invalidate existing position
				indirectionTexturePixels[lastIndex] = false;

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
			lruCache.Add(request, targetIndex);

			// Mark this pixel as filled in the array
			indirectionTexturePixels[request] = true;

			var mipFactor = 1f / (settings.VirtualResolution >> position.z);
			var uvScale = settings.TileResolution * mipFactor;
			var uvOffset = new Vector2(position.x * settings.TileResolution, position.y * settings.TileResolution) * mipFactor;

			// Set some data for the ComputeShader to update the indirectiontexture
			tileRequests.Add((uint)((targetIndex & 0xFFFF) | ((position.z & 0xFFFF) << 16)));
			destPixels.Add((uint)(position.x | (position.y << 16)));
			scaleOffsets.Add(new Vector4(uvScale, uvScale, uvOffset.x, uvOffset.y));
			dstOffsets.Add((uint)targetIndex);

			// Exit if we've reached the max number of tiles for this frame
			if (++index == settings.UpdateTileCount)
			{
				break;
			}
		}

		// TODO: Should these be texture arrays instead?
		var updateTempWidth = settings.TileResolution * settings.UpdateTileCount;
		var virtualAlbedoTemp = renderGraph.GetTexture(updateTempWidth, settings.TileResolution, GraphicsFormat.R8G8B8A8_SRGB, isRandomWrite: true);
		var virtualNormalTemp = renderGraph.GetTexture(updateTempWidth, settings.TileResolution, GraphicsFormat.R8G8B8A8_UNorm, isRandomWrite: true);
		var virtualHeightTemp = renderGraph.GetTexture(updateTempWidth, settings.TileResolution, GraphicsFormat.R8_UNorm, isRandomWrite: true);

		var scaleOffsetsBuffer = renderGraph.GetBuffer(scaleOffsets.Count, sizeof(float) * 4);
		var dstOffsetsBuffer = renderGraph.GetBuffer(dstOffsets.Count);

		var length = Math.Min(settings.UpdateTileCount, pendingRequests.Count);
		pendingRequests.Clear();

		// Build the virtual texture
		using (var pass = renderGraph.AddComputeRenderPass("Build", (scaleOffsetsBuffer, scaleOffsets, dstOffsetsBuffer, dstOffsets, settings.TileResolution)))
		{
			pass.Initialize(virtualTextureBuild, 0, settings.TileResolution * length, settings.TileResolution);

			pass.AddRenderPassData<TerrainRenderData>();
			pass.AddRenderPassData<ViewData>();

			pass.WriteTexture("_AlbedoSmoothness", virtualAlbedoTemp);
			pass.WriteTexture("_NormalMetalOcclusion", virtualNormalTemp);
			pass.WriteTexture("_Heights", virtualHeightTemp);

			pass.WriteBuffer("", dstOffsetsBuffer);
			pass.WriteBuffer("", scaleOffsetsBuffer);

			pass.ReadBuffer("_DstOffsets", dstOffsetsBuffer);
			pass.ReadBuffer("_ScaleOffsets", scaleOffsetsBuffer);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				// Upload the new positions
				command.SetBufferData(pass.GetBuffer(data.scaleOffsetsBuffer), data.scaleOffsets);
				command.SetBufferData(pass.GetBuffer(data.dstOffsetsBuffer), data.dstOffsets);

				pass.SetVector("_Resolution", new Float2(data.TileResolution, data.TileResolution));
				pass.SetInt("_Width", data.TileResolution);
			});
		}

		var albedoCompressedTemp = renderGraph.GetTexture((settings.TileResolution >> 2) * settings.UpdateTileCount, settings.TileResolution >> 2, GraphicsFormat.R32G32B32A32_UInt, hasMips: true);
		var normalCompressedTemp = renderGraph.GetTexture((settings.TileResolution >> 2) * settings.UpdateTileCount, settings.TileResolution >> 2, GraphicsFormat.R32G32B32A32_UInt, hasMips: true);
		var heightCompressedTemp = renderGraph.GetTexture((settings.TileResolution >> 2) * settings.UpdateTileCount, settings.TileResolution >> 2, GraphicsFormat.R32G32_UInt, hasMips: true);

		using (var pass = renderGraph.AddComputeRenderPass("Compress"))
		{
			pass.Initialize(dxtCompressCS, 0, (settings.TileResolution >> 2) * length, settings.TileResolution >> 2);

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

		using (var pass = renderGraph.AddGenericRenderPass("Copy", new VirtualCopyPassData(dstOffsets, settings.TileResolution, albedoCompressedTemp, normalCompressedTemp, heightCompressedTemp, albedoSmoothnessTexture, normalTexture, heightTexture)))
		{
			pass.ReadTexture("", albedoCompressedTemp);
			pass.ReadTexture("", normalCompressedTemp);
			pass.ReadTexture("", heightCompressedTemp);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				for (var j = 0; j < data.dstOffsets.Count; j++)
				{
					// Build compute shader twice, once for base mip, and once for second mip, so we can use HW trilinear filtering
					for (var i = 0; i < 2; i++)
					{
						var mipResolution = data.TileResolution >> i;

						// Copy albedo, normal and height
						command.CopyTexture(pass.GetRenderTexture(data.albedoCompressId), 0, i, j * (mipResolution >> 2), 0, mipResolution >> 2, mipResolution >> 2, data.albedoSmoothnessTexture, (int)data.dstOffsets[j], i, 0, 0);
						command.CopyTexture(pass.GetRenderTexture(data.normalCompressId), 0, i, j * (mipResolution >> 2), 0, mipResolution >> 2, mipResolution >> 2, data.normalTexture, (int)data.dstOffsets[j], i, 0, 0);
						command.CopyTexture(pass.GetRenderTexture(data.heightCompressId), 0, i, j * (mipResolution >> 2), 0, mipResolution >> 2, mipResolution >> 2, data.heightTexture, (int)data.dstOffsets[j], i, 0, 0);
					}
				}
			});
		}

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
		var mipCount = (int)Math.Log2(IndirectionTextureResolution);
		var destPixelbuffer = renderGraph.GetBuffer(destPixels.Count);

		// Only update required mips (And extents?)
		// Max(0) because highest mip might request it's parent mip too, this is easier (and fast enough)
		//var start = Math.Max(0, mipCount - updateMip);
		//for (var z = start; z < mipCount; z++)

		for (var z = 0; z <= mipCount; z++)
		{
			using (var pass = renderGraph.AddComputeRenderPass("Map New Data", (currentMip: z, maxIndex: tileRequests.Count, destPixelbuffer, destPixels)))
			{
				pass.Initialize(virtualTextureUpdateShader, 1, tileRequests.Count);
				pass.WriteTexture("DestMip", indirectionTexture, z);
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

		for (var z = mipCount - 1; z >= 0; z--)
		{
			var mipSize = IndirectionTextureResolution >> z;
			using (var pass = renderGraph.AddComputeRenderPass("Page Table Update", mipSize))
			{
				pass.Initialize(virtualTextureUpdateShader, 2, mipSize, mipSize);
				pass.WriteTexture("DestMip", indirectionTexture, z);
				pass.ReadTexture("SourceMip", indirectionTexture, z + 1);
				pass.ReadTexture("IndirectionTextureMap", indirectionTextureMapTexture, z);

				pass.SetRenderFunction(static (command, pass, mipSize) =>
				{
					pass.SetInt("MaxIndex", mipSize);
				});
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

	private struct VirtualCopyPassData
	{
		public List<uint> dstOffsets;
		public int TileResolution;
		public ResourceHandle<RenderTexture> albedoCompressId;
		public ResourceHandle<RenderTexture> normalCompressId;
		public ResourceHandle<RenderTexture> heightCompressId;
		public Texture2DArray albedoSmoothnessTexture;
		public Texture2DArray normalTexture;
		public Texture2DArray heightTexture;

		public VirtualCopyPassData(List<uint> dstOffsets, int tileResolution, ResourceHandle<RenderTexture> albedoCompressId, ResourceHandle<RenderTexture> normalCompressId, ResourceHandle<RenderTexture> heightCompressId, Texture2DArray albedoSmoothnessTexture, Texture2DArray normalTexture, Texture2DArray heightTexture)
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

public readonly struct VirtualTextureData : IRenderPassData
{
	private readonly Texture2DArray albedoSmoothness, normal, height;
	private readonly ResourceHandle<RenderTexture> indirection;
	private readonly float anisoLevel, virtualResolution;
	private readonly Float4 indTexSize;

	public VirtualTextureData(Texture2DArray albedoSmoothness, Texture2DArray normal, Texture2DArray height, ResourceHandle<RenderTexture> indirection, float anisoLevel, float virtualResolution, Float4 indTexSize)
	{
		this.albedoSmoothness = albedoSmoothness;
		this.normal = normal;
		this.height = height;
		this.indirection = indirection;
		this.anisoLevel = anisoLevel;
		this.virtualResolution = virtualResolution;
		this.indTexSize = indTexSize;
	}

	void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadTexture("_IndirectionTexture", indirection);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetTexture("_VirtualTexture", albedoSmoothness);
		pass.SetTexture("_VirtualNormalTexture", normal);
		pass.SetTexture("_VirtualHeightTexture", height);
		pass.SetFloat("_AnisoLevel", anisoLevel);
		pass.SetFloat("_VirtualUvScale", virtualResolution);
		pass.SetVector("_IndirectionTexelSize", indTexSize);
	}
}