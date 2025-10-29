using UnityEngine;
using UnityEngine.Rendering;

public struct TerrainRenderData : IRenderPassData
{
	private static readonly int _TerrainHolesTextureId = Shader.PropertyToID("_TerrainHolesTexture");
	private static readonly int AlbedoSmoothnessId = Shader.PropertyToID("AlbedoSmoothness");
	private static readonly int NormalId = Shader.PropertyToID("Normal");
	private static readonly int MaskId = Shader.PropertyToID("Mask");

	private readonly Texture2DArray albedoSmoothness, normal, mask;
	private readonly ResourceHandle<RenderTexture> terrainHeightmapTexture, terrainNormalMap, idMap, aoMap;
	private readonly Texture terrainHolesTexture;
	private Float4 terrainRemapHalfTexel, terrainScaleOffset;
	private Float3 terrainSize, terrainPosition;
	private readonly float terrainHeightScale, terrainHeightOffset, idMapResolution;
	private readonly ResourceHandle<GraphicsBuffer> terrainLayerData;

	public TerrainRenderData(Texture2DArray albedoSmoothness, Texture2DArray normal, Texture2DArray mask, ResourceHandle<RenderTexture> terrainHeightmapTexture, ResourceHandle<RenderTexture> terrainNormalMap, ResourceHandle<RenderTexture> idMap, Texture terrainHolesTexture, Float4 terrainRemapHalfTexel, Float4 terrainScaleOffset, Float3 terrainSize, float terrainHeightScale, float terrainHeightOffset, float idMapResolution, ResourceHandle<GraphicsBuffer> terrainLayerData, ResourceHandle<RenderTexture> aoMap, Float3 terrainPosition)
	{
		this.albedoSmoothness = albedoSmoothness;
		this.normal = normal;
		this.mask = mask;
		this.terrainHeightmapTexture = terrainHeightmapTexture;
		this.terrainNormalMap = terrainNormalMap;
		this.idMap = idMap;
		this.terrainHolesTexture = terrainHolesTexture;
		this.terrainRemapHalfTexel = terrainRemapHalfTexel;
		this.terrainScaleOffset = terrainScaleOffset;
		this.terrainSize = terrainSize;
		this.terrainHeightScale = terrainHeightScale;
		this.terrainHeightOffset = terrainHeightOffset;
		this.idMapResolution = idMapResolution;
		this.terrainLayerData = terrainLayerData;
		this.aoMap = aoMap;
		this.terrainPosition = terrainPosition;
	}

	public readonly void SetInputs(RenderPass pass)
	{
		pass.ReadTexture("TerrainHeightmap", terrainHeightmapTexture);
		pass.ReadTexture("TerrainNormalMap", terrainNormalMap);
		pass.ReadTexture("IdMap", idMap);
		pass.ReadTexture("BentNormalVisibility", aoMap);
		pass.ReadBuffer("TerrainLayerData", terrainLayerData);
	}

	public readonly void SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetTexture(_TerrainHolesTextureId, terrainHolesTexture);

		pass.SetTexture(AlbedoSmoothnessId, albedoSmoothness);
		pass.SetTexture(NormalId, normal);
		pass.SetTexture(MaskId, mask);

		pass.SetVector("TerrainSize", terrainSize);
		pass.SetVector("TerrainPosition", terrainPosition);
		pass.SetVector("_TerrainRemapHalfTexel", terrainRemapHalfTexel);
		pass.SetVector("_TerrainScaleOffset", terrainScaleOffset);

		pass.SetFloat("IdMapResolution", idMapResolution);
		pass.SetFloat("_TerrainHeightScale", terrainHeightScale);
		pass.SetFloat("_TerrainHeightOffset", terrainHeightOffset);
	}
}
