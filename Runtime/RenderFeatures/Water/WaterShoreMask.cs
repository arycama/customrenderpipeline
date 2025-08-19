using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class WaterShoreMask : FrameRenderFeature
{
    [Serializable]
    public class Settings
    {
        [field: SerializeField] public int Resolution = 512;
    }

    private readonly Settings settings;
    private Terrain terrain;
    private readonly Material material;
    private ResourceHandle<RenderTexture> result;
    private readonly ResourceHandle<GraphicsBuffer> resultDataBuffer;
    private int version = 0, lastVersion = -1;

    public WaterShoreMask(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        this.settings = settings;
        material = new Material(Shader.Find("Hidden/WaterShoreMask")) { hideFlags = HideFlags.HideAndDontSave };
        resultDataBuffer = renderGraph.BufferHandleSystem.ImportResource(new GraphicsBuffer(GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination, 1, UnsafeUtility.SizeOf<ResultData>()));

        //TerrainCallbacks.heightmapChanged += TerrainHeightmapChanged;
    }

    protected override void Cleanup(bool disposing)
    {
        //TerrainCallbacks.heightmapChanged -= TerrainHeightmapChanged;
    }

    private void TerrainHeightmapChanged(Terrain terrain, RectInt heightRegion, bool synched)
    {
        if (terrain == this.terrain)
            version++;
    }

    public override void Render(ScriptableRenderContext context)
    {
        // TODO: Also check if properties have changed such as size etc.
        if ((Terrain.activeTerrain == terrain && version == lastVersion) || Terrain.activeTerrain == null)
            return;

        if (terrain != null)
            renderGraph.ReleasePersistentResource(result);

        lastVersion = version;
        terrain = Terrain.activeTerrain;

        var terrainPosition = terrain.transform.position;
        var terrainData = terrain.terrainData;
        var terrainSize = terrainData.size;

        // Seed pixels
        var heightmapResolution = terrainData.heightmapResolution;
        var heightmapTexture = terrainData.heightmapTexture;
        var cutoff = Mathf.InverseLerp(terrainPosition.y, terrainPosition.y + terrainSize.y * 2.0f, 0.0f);
        var invResolution = 1.0f / heightmapResolution;

        var src = renderGraph.GetTexture(heightmapResolution, heightmapResolution, GraphicsFormat.R32G32_SFloat);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Water Shore Mask Blit"))
        {
            pass.Initialize(material);
            pass.WriteTexture(src, RenderBufferLoadAction.DontCare);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("Cutoff", cutoff);
                pass.SetFloat("InvResolution", invResolution);
                pass.SetFloat("Resolution", heightmapResolution);
                pass.SetTexture("Heightmap", terrainData.heightmapTexture);
            });
        }

        // Jump flood, Ping pong between two temporary textures.
        var passes = Mathf.CeilToInt(Mathf.Log(heightmapResolution, 2));
        var minMaxValues = renderGraph.GetBuffer(4, sizeof(float), GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource);
        for (var i = 0; i < passes; i++)
        {
            var offset = Mathf.Pow(2, passes - i - 1);
            var dst = renderGraph.GetTexture(heightmapResolution, heightmapResolution, GraphicsFormat.R32G32_SFloat);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Water Shore Mask Jump Flood"))
            {
                pass.Initialize(material, 1);

                if (i == passes - 1)
                {
                    pass.Keyword = "FINAL_PASS";
                    pass.WriteBuffer("MinMaxValuesWrite", minMaxValues);
                }

                pass.ReadTexture("JumpFloodInput", src);
                pass.WriteTexture(dst, RenderBufferLoadAction.DontCare);

                var index = i;
                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("Offset", offset);
                    pass.SetTexture("Heightmap", terrainData.heightmapTexture);
                    pass.SetFloat("InvResolution", invResolution);
                    pass.SetFloat("Resolution", heightmapResolution);
                    pass.SetFloat("Cutoff", cutoff);

                    if (index == passes - 1)
                    {
                        var testData = ArrayPool<int>.Get(4);
                        testData[0] = 0;
                        testData[1] = 0;
                        testData[2] = 0;
                        testData[3] = 0;
                        command.SetBufferData(pass.GetBuffer(minMaxValues), testData);
                        command.SetRandomWriteTarget(1, pass.GetBuffer(minMaxValues));
                        ArrayPool<int>.Release(testData);
                    }
                });
            }

            src = dst;
        }

        // Final combination pass
        result = renderGraph.GetTexture(heightmapResolution, heightmapResolution, GraphicsFormat.R16G16B16A16_UNorm, isPersistent: true);

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Water Shore Final Combine"))
        {
            pass.Initialize(material, 2);
            pass.ReadTexture("JumpFloodInput", src);
            pass.WriteTexture(result);
            pass.ReadBuffer("MinMaxValues", minMaxValues);
            pass.WriteBuffer("", resultDataBuffer);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetTexture("Heightmap", heightmapTexture);
                pass.SetFloat("Cutoff", cutoff);
                pass.SetFloat("InvResolution", invResolution);
                command.CopyBuffer(pass.GetBuffer(minMaxValues), pass.GetBuffer(resultDataBuffer));
                pass.SetFloat("Resolution", heightmapResolution);
            });
        }

        var scaleOffset = new Vector4(1f / terrainSize.x, 1f / terrainSize.z, -terrainPosition.x / terrainSize.x, -terrainPosition.z / terrainSize.z);
        renderGraph.SetResource<Result>(new Result(result, resultDataBuffer, scaleOffset, terrainSize, -terrainPosition.y, terrainSize.x), true);
    }

    private readonly struct ResultData
    {
        public readonly float minDist, maxDist, padding0, padding1;

        public ResultData(float minDist, float maxDist)
        {
            this.minDist = minDist;
            this.maxDist = maxDist;
            padding0 = 0;
            padding1 = 0;
        }
    }

    public struct Result : IRenderPassData
    {
        private readonly ResourceHandle<RenderTexture> shoreDistance;
        private readonly ResourceHandle<GraphicsBuffer> resultDataBuffer;
        private Vector4 scaleOffset;
        private Vector2 terrainSize;
        private readonly float maxOceanDepth, maxTerrainDistance;

        public Result(ResourceHandle<RenderTexture> shoreDistance, ResourceHandle<GraphicsBuffer> resultDataBuffer, Vector4 scaleOffset, Vector2 terrainSize, float maxOceanDepth, float maxTerrainDistance)
        {
            this.shoreDistance = shoreDistance;
            this.resultDataBuffer = resultDataBuffer;
            this.scaleOffset = scaleOffset;
            this.terrainSize = terrainSize;
            this.maxOceanDepth = maxOceanDepth;
            this.maxTerrainDistance = maxTerrainDistance;
        }

		void IRenderPassData.SetInputs(RenderPassBase pass)
		{
			pass.ReadTexture("ShoreDistance", shoreDistance);
			pass.ReadBuffer("WaterShoreMaskProperties", resultDataBuffer);
		}

		void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
		{
			pass.SetVector("ShoreScaleOffset", scaleOffset);
			pass.SetVector("ShoreTerrainSize", terrainSize);
			pass.SetFloat("ShoreMaxOceanDepth", maxOceanDepth);
			pass.SetFloat("ShoreMaxTerrainDistance", maxTerrainDistance);
		}
	}
}