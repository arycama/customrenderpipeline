using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class WaterShoreMask
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField] public int Resolution = 512;
        }

        private RenderGraph renderGraph;
        private Settings settings;
        private Terrain terrain;
        private Material material;
        private RTHandle result;
        private BufferHandle resultDataBuffer;
        private int version = 0, lastVersion = -1;

        public WaterShoreMask(RenderGraph renderGraph, Settings settings)
        {
            this.renderGraph = renderGraph;
            this.settings = settings;
            this.material = new Material(Shader.Find("Hidden/WaterShoreMask")) { hideFlags = HideFlags.HideAndDontSave };
            resultDataBuffer = renderGraph.ImportBuffer(new GraphicsBuffer(GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination, 1, UnsafeUtility.SizeOf<ResultData>()));

            TerrainCallbacks.heightmapChanged += TerrainHeightmapChanged;
        }

        ~WaterShoreMask()
        {
            TerrainCallbacks.heightmapChanged -= TerrainHeightmapChanged;
        }

        private void TerrainHeightmapChanged(Terrain terrain, RectInt heightRegion, bool synched)
        {
            if (terrain == this.terrain)
                version++;
        }

        public void Render()
        {
            // TODO: Also check if properties have changed such as size etc.
            if ((Terrain.activeTerrain == terrain && version == lastVersion) || Terrain.activeTerrain == null)
                return;

            if (terrain != null)
                result.IsPersistent = false;

            Debug.Log("Updating Shore Mask");

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

                var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    pass.SetFloat(command, "Cutoff", cutoff);
                    pass.SetFloat(command, "InvResolution", invResolution);
                    pass.SetFloat(command, "Resolution", heightmapResolution);
                    pass.SetTexture(command, "Heightmap", terrainData.heightmapTexture);
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
                    var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                    {
                        pass.SetFloat(command, "Offset", offset);
                        pass.SetTexture(command, "Heightmap", terrainData.heightmapTexture);
                        pass.SetFloat(command, "InvResolution", invResolution);
                        pass.SetFloat(command, "Resolution", heightmapResolution);
                        pass.SetFloat(command, "Cutoff", cutoff);

                        if (index == passes - 1)
                        {
                            var testData = ArrayPool<int>.Get(4);
                            testData[0] = 0;
                            testData[1] = 0;
                            testData[2] = 0;
                            testData[3] = 0;
                            command.SetBufferData(minMaxValues, testData);
                            command.SetRandomWriteTarget(1, minMaxValues);
                            ArrayPool<int>.Release(testData);
                        }
                    });
                }

                src = dst;
            }

            // Final combination pass
            result = renderGraph.GetTexture(heightmapResolution, heightmapResolution, GraphicsFormat.R16G16B16A16_UNorm, isExactSize: true, isPersistent: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Water Shore Final Combine"))
            {
                pass.Initialize(material, 2);
                pass.ReadTexture("JumpFloodInput", src);
                pass.WriteTexture(result);
                pass.ReadBuffer("MinMaxValues", minMaxValues);

                var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    pass.SetTexture(command, "Heightmap", heightmapTexture);
                    pass.SetFloat(command, "Cutoff", cutoff);
                    pass.SetFloat(command, "InvResolution", invResolution);
                    command.CopyBuffer(minMaxValues, resultDataBuffer);
                    pass.SetFloat(command, "Resolution", heightmapResolution);
                });
            }

            var scaleOffset = new Vector4(1f / terrainSize.x, 1f / terrainSize.z, -terrainPosition.x / terrainSize.x, -terrainPosition.z / terrainSize.z);
            renderGraph.ResourceMap.SetRenderPassData<Result>(new Result(result, resultDataBuffer, scaleOffset, terrainSize, -terrainPosition.y, terrainSize.x), renderGraph.FrameIndex, true);
        }

        struct ResultData
        {
            float minDist, maxDist, padding0, padding1;
         
            public ResultData(float minDist, float maxDist)
            {
                this.minDist = minDist;
                this.maxDist = maxDist;
                this.padding0 = 0;
                this.padding1 = 0;
            }
        }

        public struct Result : IRenderPassData
        {
            RTHandle shoreDistance;
            BufferHandle resultDataBuffer;
            Vector4 scaleOffset;
            Vector2 terrainSize;
            float maxOceanDepth, maxTerrainDistance;

            public Result(RTHandle shoreDistance, BufferHandle resultDataBuffer, Vector4 scaleOffset, Vector2 terrainSize, float maxOceanDepth, float maxTerrainDistance)
            {
                this.shoreDistance = shoreDistance ?? throw new ArgumentNullException(nameof(shoreDistance));
                this.resultDataBuffer = resultDataBuffer;
                this.scaleOffset = scaleOffset;
                this.terrainSize = terrainSize;
                this.maxOceanDepth = maxOceanDepth;
                this.maxTerrainDistance = maxTerrainDistance;
            }

            void IRenderPassData.SetInputs(RenderPass pass)
            {
                pass.ReadTexture("ShoreDistance", shoreDistance);
            }

            void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetConstantBuffer(command, "WaterShoreMaskProperties", resultDataBuffer);
                pass.SetVector(command, "ShoreScaleOffset", scaleOffset);
                pass.SetVector(command, "ShoreTerrainSize", terrainSize);
                pass.SetFloat(command, "ShoreMaxOceanDepth", maxOceanDepth);
                pass.SetFloat(command, "ShoreMaxTerrainDistance", maxTerrainDistance);
            }
        }
    }
}