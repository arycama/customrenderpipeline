using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public partial class WaterSystem : RenderFeature<(Camera camera, RTHandle cameraDepth, int screenWidth, int screenHeight, RTHandle velocity, CullingPlanes cullingPlanes)>
    {
        private readonly Settings settings;
        private readonly GraphicsBuffer indexBuffer;

        public WaterFft WaterFft { get; }
        private readonly UnderwaterLighting underwaterLighting;
        private readonly DeferredWater deferredWater;
        private readonly WaterCaustics caustics;

        private int VerticesPerTileEdge => settings.PatchVertices + 1;
        private int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;

        public WaterSystem(RenderGraph renderGraph, Settings settings) : base(renderGraph)
        {
            this.settings = settings;

            indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, QuadListIndexCount, sizeof(ushort)) { name = "Water System Index Buffer" };

            var index = 0;
            var pIndices = new ushort[QuadListIndexCount];
            for (var y = 0; y < settings.PatchVertices; y++)
            {
                var rowStart = y * VerticesPerTileEdge;

                for (var x = 0; x < settings.PatchVertices; x++)
                {
                    // Can do a checkerboard flip to avoid directioanl artifacts, but will mess with the tessellation code
                    //var flip = (x & 1) == (y & 1);

                    //if(flip)
                    //{
                    pIndices[index++] = (ushort)(rowStart + x);
                    pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge);
                    pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge + 1);
                    pIndices[index++] = (ushort)(rowStart + x + 1);
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

            WaterFft = new(renderGraph, settings);
            underwaterLighting = new(renderGraph, settings);
            deferredWater = new(renderGraph, settings);
            caustics = new(renderGraph, settings);
        }

        protected override void Cleanup(bool disposing)
        {
            indexBuffer.Dispose();

            WaterFft.Dispose();
            underwaterLighting.Dispose();
            deferredWater.Dispose();
            caustics.Dispose();
        }

        public void UpdateCaustics()
        {
            caustics.Render();
        }

        public void CullShadow(Vector3 viewPosition, CullingResults cullingResults)
        {
            if (!settings.IsEnabled)
                return;

            var lightRotation = Quaternion.identity;
            for (var i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                var visibleLight = cullingResults.visibleLights[i];
                if (visibleLight.lightType != LightType.Directional)
                    continue;

                lightRotation = visibleLight.localToWorldMatrix.rotation;
                break;
            }

            // TODO: Should be able to simply just define a box and not even worry about view position since we translate it anyway
            var size = new Vector3(settings.ShadowRadius * 2, settings.Profile.MaxWaterHeight * 2, settings.ShadowRadius * 2);
            var min = new Vector3(-settings.ShadowRadius, -settings.Profile.MaxWaterHeight - viewPosition.y, -settings.ShadowRadius);

            var texelSize = settings.ShadowRadius * 2.0f / settings.ShadowResolution;

            var snappedViewPositionX = MathUtils.Snap(viewPosition.x, texelSize) - viewPosition.x;
            var snappedViewPositionZ = MathUtils.Snap(viewPosition.z, texelSize) - viewPosition.z;

            var worldToLight = Matrix4x4.Rotate(Quaternion.Inverse(lightRotation));
            Vector3 minValue = Vector3.positiveInfinity, maxValue = Vector3.negativeInfinity;

            for (var z = 0; z < 2; z++)
            {
                for (var y = 0; y < 2; y++)
                {
                    for (var x = 0; x < 2; x++)
                    {
                        var worldPosition = min + Vector3.Scale(size, new Vector3(x, y, z));
                        worldPosition.x += snappedViewPositionX;
                        worldPosition.z += snappedViewPositionZ;

                        var localPosition = worldToLight.MultiplyPoint3x4(worldPosition);
                        minValue = Vector3.Min(minValue, localPosition);
                        maxValue = Vector3.Max(maxValue, localPosition);
                    }
                }
            }

            // Calculate culling planes
            var width = maxValue.x - minValue.x;
            var height = maxValue.y - minValue.y;
            var depth = maxValue.z - minValue.z;
            var projectionMatrix = new Matrix4x4
            {
                m00 = 2.0f / width,
                m03 = (maxValue.x + minValue.x) / -width,
                m11 = -2.0f / height,
                m13 = -(maxValue.y + minValue.y) / -height,
                m22 = 1.0f / (minValue.z - maxValue.z),
                m23 = maxValue.z / depth,
                m33 = 1.0f
            };

            var viewProjectionMatrix = projectionMatrix * worldToLight;

            var frustumPlanes = ArrayPool<Plane>.Get(6);
            GeometryUtility.CalculateFrustumPlanes(viewProjectionMatrix, frustumPlanes);

            var cullingPlanes = new CullingPlanes() { Count = 6 };
            for (var j = 0; j < 6; j++)
            {
                cullingPlanes.SetCullingPlane(j, frustumPlanes[j]);
            }

            ArrayPool<Plane>.Release(frustumPlanes);

            var cullResult = Cull(viewPosition, cullingPlanes);

            var vm = worldToLight;
            var shadowMatrix = new Matrix4x4
            {
                m00 = vm.m00 / width,
                m01 = vm.m01 / width,
                m02 = vm.m02 / width,
                m03 = (vm.m03 - 0.5f * (maxValue.x + minValue.x)) / width + 0.5f,

                m10 = vm.m10 / height,
                m11 = vm.m11 / height,
                m12 = vm.m12 / height,
                m13 = (vm.m13 - 0.5f * (maxValue.y + minValue.y)) / height + 0.5f,

                m20 = -vm.m20 / depth,
                m21 = -vm.m21 / depth,
                m22 = -vm.m22 / depth,
                m23 = (-vm.m23 + 0.5f * (maxValue.z + minValue.z)) / depth + 0.5f,

                m33 = 1.0f
            };

            // TODO: Change to near/far
            renderGraph.ResourceMap.SetRenderPassData(new WaterShadowCullResult(cullResult.IndirectArgsBuffer, cullResult.PatchDataBuffer, 0.0f, maxValue.z - minValue.z, viewProjectionMatrix, shadowMatrix, cullingPlanes), renderGraph.FrameIndex);
        }

        public void CullRender(Vector3 viewPosition, CullingPlanes cullingPlanes)
        {
            if (!settings.IsEnabled)
                return;

            var result = Cull(viewPosition, cullingPlanes);
            renderGraph.ResourceMap.SetRenderPassData(new WaterRenderCullResult(result.IndirectArgsBuffer, result.PatchDataBuffer), renderGraph.FrameIndex);
        }

        public void RenderShadow(Vector3 viewPosition)
        {
            if (!settings.IsEnabled)
                return;

            var waterShadow = renderGraph.GetTexture(settings.ShadowResolution, settings.ShadowResolution, GraphicsFormat.D16_UNorm);
            var waterIlluminance = renderGraph.GetTexture(settings.ShadowResolution, settings.ShadowResolution, GraphicsFormat.R16_UNorm);

            var passIndex = settings.Material.FindPass("WaterShadow");
            Assert.IsTrue(passIndex != -1, "Water Material does not contain a Water Shadow Pass");

            var profile = settings.Profile;
            var resolution = settings.Resolution;

            var passData = renderGraph.ResourceMap.GetRenderPassData<WaterShadowCullResult>(renderGraph.FrameIndex);
            using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Ocean Shadow"))
            {
                pass.Initialize(settings.Material, indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex, depthBias: settings.ShadowBias, slopeDepthBias: settings.ShadowSlopeBias);
                pass.WriteDepth(waterShadow);
                pass.WriteTexture(waterIlluminance, RenderBufferLoadAction.DontCare);
                pass.ConfigureClear(RTClearFlags.Depth);
                pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);

                pass.AddRenderPassData<OceanFftResult>();
                pass.AddRenderPassData<WaterShoreMask.Result>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<DirectionalLightInfo>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetMatrix(command, "_WaterShadowMatrix", passData.WorldToClip);
                    pass.SetInt(command, "_VerticesPerEdge", VerticesPerTileEdge);
                    pass.SetInt(command, "_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
                    pass.SetFloat(command, "_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));

                    // Snap to quad-sized increments on largest cell
                    var texelSize = settings.Size / (float)settings.PatchVertices;
                    var positionX = MathUtils.Snap(viewPosition.x, texelSize) - viewPosition.x - settings.Size * 0.5f;
                    var positionZ = MathUtils.Snap(viewPosition.z, texelSize) - viewPosition.z - settings.Size * 0.5f;
                    pass.SetVector(command, "_PatchScaleOffset", new Vector4(settings.Size / (float)settings.CellCount, settings.Size / (float)settings.CellCount, positionX, positionZ));

                    var cullingPlanesArray = ArrayPool<Vector4>.Get(passData.CullingPlanes.Count);
                    for (var i = 0; i < passData.CullingPlanes.Count; i++)
                        cullingPlanesArray[i] = passData.CullingPlanes.GetCullingPlaneVector4(i);

                    pass.SetVectorArray(command, "_CullingPlanes", cullingPlanesArray);
                    ArrayPool<Vector4>.Release(cullingPlanesArray);

                    pass.SetInt(command, "_CullingPlanesCount", passData.CullingPlanes.Count);
                    pass.SetFloat(command, "_ShoreWaveWindSpeed", settings.Profile.WindSpeed);
                    pass.SetFloat(command, "_ShoreWaveWindAngle", settings.Profile.WindAngle);
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new WaterShadowResult(waterShadow, passData.ShadowMatrix, passData.Near, passData.Far, settings.Material.GetVector("_Extinction"), waterIlluminance), renderGraph.FrameIndex);
        }


        public override void Render((Camera camera, RTHandle cameraDepth, int screenWidth, int screenHeight, RTHandle velocity, CullingPlanes cullingPlanes) data)
        {
            var viewPosition = data.camera.transform.position;
            if (!settings.IsEnabled)
                return;

            // Writes (worldPos - displacementPos).xz. Uv coord is reconstructed later from delta and worldPosition (reconstructed from depth)
            var oceanRenderResult = renderGraph.GetTexture(data.screenWidth, data.screenHeight, GraphicsFormat.R16G16_SFloat, isScreenTexture: true);

            // Also write triangleNormal to another texture with oct encoding. This allows reconstructing the derivative correctly to avoid mip issues on edges,
            // As well as backfacing triangle detection for rendering under the surface
            var waterTriangleNormal = renderGraph.GetTexture(data.screenWidth, data.screenHeight, GraphicsFormat.R16G16_UNorm, isScreenTexture: true);

            var passIndex = settings.Material.FindPass("Water");
            Assert.IsTrue(passIndex != -1, "Water Material has no Water Pass");

            var profile = settings.Profile;
            var resolution = settings.Resolution;

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Ocean Clear Pass"))
            {
                pass.SetRenderFunction((command, pass) =>
                {
                    command.SetRenderTarget(waterTriangleNormal);
                    command.ClearRenderTarget(false, true, Color.clear);
                });
            }


            using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Ocean Render"))
            {
                var passData = renderGraph.ResourceMap.GetRenderPassData<WaterRenderCullResult>(renderGraph.FrameIndex);
                pass.Initialize(settings.Material, indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);

                pass.WriteDepth(data.cameraDepth);
                pass.WriteTexture(oceanRenderResult, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(data.velocity);
                pass.WriteTexture(waterTriangleNormal, RenderBufferLoadAction.DontCare);

                pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);

                pass.AddRenderPassData<OceanFftResult>();
                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<TemporalAA.TemporalAAData>();
                pass.AddRenderPassData<WaterShoreMask.Result>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt(command, "_VerticesPerEdge", VerticesPerTileEdge);
                    pass.SetInt(command, "_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
                    pass.SetFloat(command, "_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));
                    pass.SetInt(command, "_OceanTextureSlicePreviousOffset", ((renderGraph.FrameIndex & 1) == 0) ? 0 : 4);

                    // Snap to quad-sized increments on largest cell
                    var texelSize = settings.Size / (float)settings.PatchVertices;
                    var positionX = MathUtils.Snap(viewPosition.x, texelSize) - viewPosition.x - settings.Size * 0.5f;
                    var positionZ = MathUtils.Snap(viewPosition.z, texelSize) - viewPosition.z - settings.Size * 0.5f;
                    pass.SetVector(command, "_PatchScaleOffset", new Vector4(settings.Size / (float)settings.CellCount, settings.Size / (float)settings.CellCount, positionX, positionZ));

                    pass.SetInt(command, "_CullingPlanesCount", data.cullingPlanes.Count);
                    var cullingPlanesArray = ArrayPool<Vector4>.Get(data.cullingPlanes.Count);
                    for (var i = 0; i < data.cullingPlanes.Count; i++)
                        cullingPlanesArray[i] = data.cullingPlanes.GetCullingPlaneVector4(i);

                    pass.SetVectorArray(command, "_CullingPlanes", cullingPlanesArray);
                    ArrayPool<Vector4>.Release(cullingPlanesArray);

                    pass.SetFloat(command, "_ShoreWaveWindSpeed", settings.Profile.WindSpeed);
                    pass.SetFloat(command, "_ShoreWaveWindAngle", settings.Profile.WindAngle);
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new WaterPrepassResult(oceanRenderResult, waterTriangleNormal, (Vector4)settings.Material.GetColor("_Color").linear, (Vector4)settings.Material.GetColor("_Extinction")), renderGraph.FrameIndex);
        }

        public void RenderWaterPost(int screenWidth, int screenHeight, RTHandle underwaterDepth, RTHandle cameraDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, Camera camera)
        {
            if (!settings.IsEnabled)
                return;

            underwaterLighting.Render((screenWidth, screenHeight, underwaterDepth, cameraDepth, albedoMetallic, normalRoughness, bentNormalOcclusion, emissive));
            deferredWater.Render((underwaterDepth, albedoMetallic, normalRoughness, bentNormalOcclusion, emissive, cameraDepth, camera, screenWidth, screenHeight));
        }

        private WaterCullResult Cull(Vector3 viewPosition, CullingPlanes cullingPlanes)
        {
            // TODO: Preload?
            var compute = Resources.Load<ComputeShader>("OceanQuadtreeCull");
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
                using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Quadtree Cull"))
                {
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

                    // pass.AddKeyword("NO_HEIGHTS");

                    if (isFinalPass && !isFirstPass)
                    {
                        // Final pass writes out lods to a temp texture if more than one pass was used
                        pass.WriteTexture("_LodResult", tempLodId);
                    }

                    if (!isFinalPass)
                        pass.WriteTexture("_TempResultWrite", tempIds[i]);

                    pass.WriteBuffer("_IndirectArgs", indirectArgsBuffer);
                    pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);

                    pass.AddRenderPassData<ICommonPassData>();

                    var index = i;
                    pass.SetRenderFunction((command, pass) =>
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

                        // Do up to 6 passes per dispatch.
                        pass.SetInt(command, "_PassCount", passCount);
                        pass.SetInt(command, "_PassOffset", 6 * index);
                        pass.SetInt(command, "_TotalPassCount", totalPassCount);

                        var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
                        for (var i = 0; i < cullingPlanes.Count; i++)
                            cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

                        pass.SetVectorArray(command, "_CullingPlanes", cullingPlanesArray);
                        ArrayPool<Vector4>.Release(cullingPlanesArray);

                        // Snap to quad-sized increments on largest cell
                        var texelSize = settings.Size / (float)settings.PatchVertices;
                        var positionX = MathUtils.Snap(viewPosition.x, texelSize) - viewPosition.x - settings.Size * 0.5f;
                        var positionZ = MathUtils.Snap(viewPosition.z, texelSize) - viewPosition.z - settings.Size * 0.5f;

                        var positionOffset = new Vector4(settings.Size, settings.Size, positionX, positionZ);
                        pass.SetVector(command, "_TerrainPositionOffset", positionOffset);

                        pass.SetFloat(command, "_EdgeLength", (float)settings.EdgeLength * settings.PatchVertices);
                        pass.SetInt(command, "_CullingPlanesCount", cullingPlanes.Count);
                        pass.SetFloat(command, "MaxWaterHeight", settings.Profile.MaxWaterHeight);
                    });
                }
            }

            if (dispatchCount > 1)
            {
                using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Quadtree Cull"))
                {
                    pass.Initialize(compute, 1, normalizedDispatch: false);
                    pass.WriteBuffer("_IndirectArgs", lodIndirectArgsBuffer);

                    // If more than one pass needed, we need a second pass to write out lod deltas to the patch data
                    // Copy count from indirect draw args so we only dispatch as many threads as needed
                    pass.ReadBuffer("_IndirectArgsInput", indirectArgsBuffer);
                }

                using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Ocean Quadtree Cull"))
                {
                    pass.Initialize(compute, lodIndirectArgsBuffer, 2);
                    pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);
                    pass.ReadTexture("_LodInput", tempLodId);
                    pass.ReadBuffer("_IndirectArgs", indirectArgsBuffer);

                    pass.SetRenderFunction((command, pass) =>
                    {
                        pass.SetInt(command, "_CellCount", settings.CellCount);
                    });
                }
            }

            ListPool<RTHandle>.Release(tempIds);

            return new(indirectArgsBuffer, patchDataBuffer);
        }

    }
}