using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class OceanSystem
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField, Tooltip("The resolution of the simulation, higher numbers give more detail but are more expensive")] public int Resolution { get; private set; } = 128;
            [field: SerializeField, Tooltip("Use Trilinear for the normal/foam map, improves quality of lighting/reflections in shader")] public bool UseTrilinear { get; private set; } = true;
            [field: SerializeField, Range(1, 16), Tooltip("Anisotropic level for the normal/foam map")] public int AnisoLevel { get; private set; } = 4;
            [field: SerializeField] public Material Material { get; private set; }
            [field: SerializeField] public WaterProfile Profile { get; private set; }
            [field: SerializeField] public float ShadowRadius { get; private set; } = 8192;
            [field: SerializeField] public int ShadowResolution { get; private set; } = 512;

            [field: Header("Rendering")]
            [field: SerializeField] public int CellCount { get; private set; } = 32;
            [field: SerializeField, Tooltip("Size of the Mesh in World Space")] public int Size { get; private set; } = 256;
            [field: SerializeField] public int PatchVertices { get; private set; } = 32;
            [field: SerializeField, Range(1, 128)] public float EdgeLength { get; private set; } = 64;
        }

        private static readonly IndexedShaderPropertyId smoothnessMapIds = new("SmoothnessOutput");

        private RTHandle lengthToRoughness;
        private Settings settings;
        private RenderGraph renderGraph;
        private Material underwaterLightingMaterial, deferredWaterMaterial;
        private GraphicsBuffer indexBuffer;
        private bool isInitialized;

        private int VerticesPerTileEdge => settings.PatchVertices + 1;
        private int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;

        private RTHandle displacementCurrent;

        public OceanSystem(RenderGraph renderGraph, Settings settings)
        {
            this.renderGraph = renderGraph;
            this.settings = settings;

            underwaterLightingMaterial = new Material(Shader.Find("Hidden/Underwater Lighting 1")) { hideFlags = HideFlags.HideAndDontSave };
            deferredWaterMaterial = new Material(Shader.Find("Hidden/Deferred Water 1")) { hideFlags = HideFlags.HideAndDontSave };

            lengthToRoughness = renderGraph.GetTexture(256, 1, GraphicsFormat.R16_UNorm, isPersistent: true);
            indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, QuadListIndexCount, sizeof(ushort));

            int index = 0;
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
        }

        public void Initialize()
        {
            if (isInitialized)
                return;

            isInitialized = true;

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Generate Length to Smoothness"))
            {
                var computeShader = Resources.Load<ComputeShader>("Utility/SmoothnessFilter");

                var generateLengthToSmoothnessKernel = computeShader.FindKernel("GenerateLengthToSmoothness");
                pass.Initialize(computeShader, generateLengthToSmoothnessKernel, 256, 1, 1, false);

                pass.WriteTexture("_LengthToRoughnessResult", lengthToRoughness);

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    pass.SetFloat(command, "_MaxIterations", 32);
                    pass.SetFloat(command, "_Resolution", 256);
                });
            }
        }

        private float previousTime;

        public void UpdateFft()
        {
            // Calculate constants
            var rcpScales = new Vector4(1f / Mathf.Pow(settings.Profile.CascadeScale, 0f), 1f / Mathf.Pow(settings.Profile.CascadeScale, 1f), 1f / Mathf.Pow(settings.Profile.CascadeScale, 2f), 1f / Mathf.Pow(settings.Profile.CascadeScale, 3f));
            var patchSizes = new Vector4(settings.Profile.PatchSize / Mathf.Pow(settings.Profile.CascadeScale, 0f), settings.Profile.PatchSize / Mathf.Pow(settings.Profile.CascadeScale, 1f), settings.Profile.PatchSize / Mathf.Pow(settings.Profile.CascadeScale, 2f), settings.Profile.PatchSize / Mathf.Pow(settings.Profile.CascadeScale, 3f));
            var spectrumStart = new Vector4(0, settings.Profile.MaxWaveNumber * patchSizes.y / patchSizes.x, settings.Profile.MaxWaveNumber * patchSizes.z / patchSizes.y, settings.Profile.MaxWaveNumber * patchSizes.w / patchSizes.z);
            var spectrumEnd = new Vector4(settings.Profile.MaxWaveNumber, settings.Profile.MaxWaveNumber, settings.Profile.MaxWaveNumber, settings.Resolution);
            var oceanScale = new Vector4(1f / patchSizes.x, 1f / patchSizes.y, 1f / patchSizes.z, 1f / patchSizes.w);
            var rcpTexelSizes = new Vector4(settings.Resolution / patchSizes.x, settings.Resolution / patchSizes.y, settings.Resolution / patchSizes.z, settings.Resolution / patchSizes.w);
            var texelSizes = patchSizes / settings.Resolution;

            // TODO: Put this in a common place, eg main pipeline
            var time = EditorApplication.isPlaying ? Time.time : (float)EditorApplication.timeSinceStartup;
            var deltaTime = time - previousTime;
            previousTime = time;

            // Load resources
            var computeShader = Resources.Load<ComputeShader>("OceanFFT");
            var oceanBuffer = settings.Profile.SetShaderProperties(renderGraph);

            var tempBufferID4 = renderGraph.GetTexture(settings.Resolution, settings.Resolution, GraphicsFormat.R32G32B32A32_SFloat, 4, TextureDimension.Tex2DArray);
            var tempBufferID2 = renderGraph.GetTexture(settings.Resolution, settings.Resolution, GraphicsFormat.R32G32_SFloat, 4, TextureDimension.Tex2DArray);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Fft Row"))
            {
                pass.Initialize(computeShader, 0, 1, settings.Resolution, 4, false);
                pass.WriteTexture("targetTexture", tempBufferID4);
                pass.WriteTexture("targetTexture1", tempBufferID2);
                pass.ReadBuffer("OceanData", oceanBuffer);

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    pass.SetVector(command, "_OceanScale", oceanScale);
                    pass.SetVector(command, "SpectrumStart", spectrumStart);
                    pass.SetVector(command, "SpectrumEnd", spectrumEnd);
                    pass.SetFloat(command, "_OceanGravity", settings.Profile.Gravity);
                    pass.SetFloat(command, "Time", time);
                });
            }

            var displacementHistory = displacementCurrent;
            var hasDisplacementHistory = displacementHistory != null;
            displacementCurrent = renderGraph.GetTexture(settings.Resolution, settings.Resolution, GraphicsFormat.R16G16B16A16_SFloat, 4, TextureDimension.Tex2DArray, hasMips: true, isPersistent: true);
            if (hasDisplacementHistory)
                displacementHistory.IsPersistent = false;
            else
                displacementHistory = displacementCurrent;

            var normalFoamSmoothness = renderGraph.GetTexture(settings.Resolution, settings.Resolution, GraphicsFormat.R8G8B8A8_SNorm, 4, TextureDimension.Tex2DArray, hasMips: true);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Fft Column"))
            {
                pass.Initialize(computeShader, 1, 1, settings.Resolution, 4, false);
                pass.ReadTexture("sourceTexture", tempBufferID4);
                pass.ReadTexture("sourceTexture1", tempBufferID2);
                pass.WriteTexture("DisplacementOutput", displacementCurrent);
            }

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Calculate Normals"))
            {
                pass.Initialize(computeShader, 2, settings.Resolution, settings.Resolution, 4);
                pass.WriteTexture("DisplacementInput", displacementCurrent);
                pass.WriteTexture("OceanNormalFoamSmoothness", normalFoamSmoothness);

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    pass.SetVector(command, "_CascadeTexelSizes", texelSizes);
                    pass.SetInt(command, "_OceanTextureSlicePreviousOffset", ((renderGraph.FrameIndex & 1) == 0) ? 0 : 4);
                    pass.SetFloat(command, "Smoothness", settings.Material.GetFloat("_Smoothness"));
                    pass.SetFloat(command, "_FoamStrength", settings.Profile.FoamStrength);
                    pass.SetFloat(command, "_FoamDecay", settings.Profile.FoamDecay);
                    pass.SetFloat(command, "_FoamThreshold", settings.Profile.FoamThreshold);
                    pass.SetFloat(command, "_DeltaTime", deltaTime);
                });
            }

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Generate Filtered Mips"))
            {
                pass.Initialize(computeShader, 3, (settings.Resolution * 4) >> 2, (settings.Resolution) >> 2, 1);
                pass.ReadTexture("_LengthToRoughness", lengthToRoughness);

                var mipCount = (int)Mathf.Log(settings.Resolution, 2) + 1;
                for (var j = 0; j < mipCount; j++)
                {
                    var smoothnessId = smoothnessMapIds.GetProperty(j);
                    pass.WriteTexture(smoothnessId, normalFoamSmoothness, j);
                }

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    // TODO: Do this manually? Since this will be compute shader anyway.. could do in same pass
                    command.GenerateMips(displacementCurrent);

                    pass.SetInt(command, "Size", settings.Resolution >> 2);
                    pass.SetFloat(command, "Smoothness", settings.Material.GetFloat("_Smoothness"));
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new OceanFftResult(displacementCurrent, displacementHistory, normalFoamSmoothness));
        }

        public void CullShadow(Vector3 viewPosition, CullingResults cullingResults, ICommonPassData commonPassData)
        {
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
            var min = new Vector3(viewPosition.x - settings.ShadowRadius, -settings.Profile.MaxWaterHeight, viewPosition.z - settings.ShadowRadius);

            var worldToLight = Matrix4x4.Rotate(Quaternion.Inverse(lightRotation));
            Vector3 minValue = Vector3.positiveInfinity, maxValue = Vector3.negativeInfinity;

            for (var z = 0; z < 2; z++)
            {
                for (var y = 0; y < 2; y++)
                {
                    for (var x = 0; x < 2; x++)
                    {
                        var localPosition = worldToLight.MultiplyPoint3x4(min + Vector3.Scale(size, new Vector3(x, y, z)));
                        minValue = Vector3.Min(minValue, localPosition);
                        maxValue = Vector3.Max(maxValue, localPosition);
                    }
                }
            }

            // Calculate culling planes
            var viewProjectionMatrix = Matrix4x4Extensions.OrthoOffCenter(minValue.x, maxValue.x, minValue.y, maxValue.y, minValue.z, maxValue.z) * worldToLight;
            var frustumPlanes = ArrayPool<Plane>.Get(6);
            GeometryUtility.CalculateFrustumPlanes(viewProjectionMatrix, frustumPlanes);

            var cullingPlanes = ArrayPool<Vector4>.Get(6);
            for (var j = 0; j < 6; j++)
            {
                var plane = frustumPlanes[j];
                cullingPlanes[j] = new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
            }
            ArrayPool<Plane>.Release(frustumPlanes);

            var cullResult = Cull(viewPosition, cullingPlanes, commonPassData);

            var gpuProjectionMatrix = new Matrix4x4
            {
                m00 = 2.0f / (maxValue.x - minValue.x),
                m03 = (maxValue.x + minValue.x) / (minValue.x - maxValue.x),
                m11 = -2.0f / (maxValue.y - minValue.y),
                m13 = -(maxValue.y + minValue.y) / (minValue.y - maxValue.y),
                m22 = 1.0f / (minValue.z - maxValue.z),
                m23 = maxValue.z / (maxValue.z - minValue.z),
                m33 = 1.0f
            };

            var viewMatrixRWS = Matrix4x4Extensions.WorldToLocal(-viewPosition, lightRotation);

            var vm = viewMatrixRWS;
            var shadowMatrix = new Matrix4x4
            {
                m00 = vm.m00 / (maxValue.x - minValue.x),
                m01 = vm.m01 / (maxValue.x - minValue.x),
                m02 = vm.m02 / (maxValue.x - minValue.x),
                m03 = (vm.m03 - 0.5f * (maxValue.x + minValue.x)) / (maxValue.x - minValue.x) + 0.5f,

                m10 = vm.m10 / (maxValue.y - minValue.y),
                m11 = vm.m11 / (maxValue.y - minValue.y),
                m12 = vm.m12 / (maxValue.y - minValue.y),
                m13 = (vm.m13 - 0.5f * (maxValue.y + minValue.y)) / (maxValue.y - minValue.y) + 0.5f,

                m20 = -vm.m20 / (maxValue.z - minValue.z),
                m21 = -vm.m21 / (maxValue.z - minValue.z),
                m22 = -vm.m22 / (maxValue.z - minValue.z),
                m23 = (-vm.m23 + 0.5f * (maxValue.z + minValue.z)) / (maxValue.z - minValue.z) + 0.5f,

                m33 = 1.0f
            };

            // TODO: Change to near/far
            renderGraph.ResourceMap.SetRenderPassData(new WaterShadowCullResult(cullResult.IndirectArgsBuffer, cullResult.PatchDataBuffer, 0.0f, maxValue.z - minValue.z, gpuProjectionMatrix * viewMatrixRWS, shadowMatrix));
        }

        public void RenderShadow(Vector3 viewPosition, ICommonPassData commonPassData)
        {
            var waterShadow = renderGraph.GetTexture(settings.ShadowResolution, settings.ShadowResolution, GraphicsFormat.D16_UNorm);

            var passIndex = settings.Material.FindPass("WaterShadow");
            Assert.IsTrue(passIndex != -1, "Water Material does not contain a Water Shadow Pass");

            var fftData = renderGraph.ResourceMap.GetRenderPassData<OceanFftResult>();
            var passData = renderGraph.ResourceMap.GetRenderPassData<WaterShadowCullResult>();
            using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Ocean Shadow"))
            {
                pass.Initialize(settings.Material, indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);
                pass.WriteDepth(waterShadow);
                pass.ConfigureClear(RTClearFlags.Depth);
                pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);
                commonPassData.SetInputs(pass);

                pass.ReadTexture("_OceanDisplacement", displacementCurrent);
                pass.AddRenderPassData<OceanFftResult>();

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);

                    pass.SetMatrix(command, "_WaterShadowMatrix", passData.WorldToClip);
                    pass.SetInt(command, "_VerticesPerEdge", VerticesPerTileEdge);
                    pass.SetInt(command, "_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
                    pass.SetFloat(command, "_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));

                    // Snap to quad-sized increments on largest cell
                    var texelSize = settings.Size / (float)settings.PatchVertices;
                    var positionX = Mathf.Floor((viewPosition.x - settings.Size * 0.5f) / texelSize) * texelSize - viewPosition.x;
                    var positionZ = Mathf.Floor((viewPosition.z - settings.Size * 0.5f) / texelSize) * texelSize - viewPosition.z;
                    pass.SetVector(command, "_PatchScaleOffset", new Vector4(settings.Size / (float)settings.CellCount, settings.Size / (float)settings.CellCount, positionX, positionZ));
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new WaterShadowResult(waterShadow, passData.ShadowMatrix, passData.Near, passData.Far, settings.Material.GetVector("_Extinction")));
        }

        public WaterCullResult Cull(Vector3 viewPosition, Vector4[] cullingPlanes, ICommonPassData commonPassData)
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
                        var texelSizeX = settings.Size / (float)settings.PatchVertices;
                        var texelSizeZ = settings.Size / (float)settings.PatchVertices;
                        var positionX = Mathf.Floor((viewPosition.x - settings.Size * 0.5f) / texelSizeX) * texelSizeX - viewPosition.x;
                        var positionZ = Mathf.Floor((viewPosition.z - settings.Size * 0.5f) / texelSizeZ) * texelSizeZ - viewPosition.z;
                        var positionOffset = new Vector4(settings.Size, settings.Size, positionX, positionZ);
                        pass.SetVector(command, "_TerrainPositionOffset", positionOffset);

                        pass.SetFloat(command, "_EdgeLength", (float)settings.EdgeLength * settings.PatchVertices);
                        pass.SetInt(command, "_CullingPlanesCount", cullingPlanes.Length);
                        pass.SetFloat(command, "MaxWaterHeight", settings.Profile.MaxWaterHeight);

                        ArrayPool<Vector4>.Release(cullingPlanes);
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

                    var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                    {
                        pass.SetInt(command, "_CellCount", settings.CellCount);
                    });
                }
            }

            ListPool<RTHandle>.Release(tempIds);

            return new(indirectArgsBuffer, patchDataBuffer);
        }


        public void CullRender(Vector3 viewPosition, Vector4[] cullingPlanes, ICommonPassData commonPassData)
        {
            var result = Cull(viewPosition, cullingPlanes, commonPassData);
            renderGraph.ResourceMap.SetRenderPassData(new WaterRenderCullResult(result.IndirectArgsBuffer, result.PatchDataBuffer));
        }

        public RTHandle RenderWater(Camera camera, RTHandle cameraDepth, int screenWidth, int screenHeight, RTHandle velocity, IRenderPassData commonPassData, Vector4[] cullingPlanes)
        {
            // Depth, rgba8 normalFoam, rgba8 roughness, mask? 
            // Writes depth, stencil, and RGBA8 containing normalRG, roughness and foam
            var oceanRenderResult = renderGraph.GetTexture(screenWidth, screenHeight, GraphicsFormat.R32G32_SFloat, isScreenTexture: true);

            var passIndex = settings.Material.FindPass("Water");
            Assert.IsTrue(passIndex != -1, "Water Material has no Water Pass");

            var profile = settings.Profile;
            var resolution = settings.Resolution;

            // Calculate constants
            var rcpScales = new Vector4(1f / Mathf.Pow(profile.CascadeScale, 0f), 1f / Mathf.Pow(profile.CascadeScale, 1f), 1f / Mathf.Pow(profile.CascadeScale, 2f), 1f / Mathf.Pow(profile.CascadeScale, 3f));
            var patchSizes = new Vector4(profile.PatchSize / Mathf.Pow(profile.CascadeScale, 0f), profile.PatchSize / Mathf.Pow(profile.CascadeScale, 1f), profile.PatchSize / Mathf.Pow(profile.CascadeScale, 2f), profile.PatchSize / Mathf.Pow(profile.CascadeScale, 3f));
            var spectrumStart = new Vector4(0, profile.MaxWaveNumber * patchSizes.y / patchSizes.x, profile.MaxWaveNumber * patchSizes.z / patchSizes.y, profile.MaxWaveNumber * patchSizes.w / patchSizes.z);
            var spectrumEnd = new Vector4(profile.MaxWaveNumber, profile.MaxWaveNumber, profile.MaxWaveNumber, resolution);
            var oceanScale = new Vector4(1f / patchSizes.x, 1f / patchSizes.y, 1f / patchSizes.z, 1f / patchSizes.w);
            var rcpTexelSizes = new Vector4(resolution / patchSizes.x, resolution / patchSizes.y, resolution / patchSizes.z, resolution / patchSizes.w);
            var texelSizes = patchSizes / resolution;

            using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Ocean Render"))
            {
                var passData = renderGraph.ResourceMap.GetRenderPassData<WaterRenderCullResult>();
                pass.Initialize(settings.Material, indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);

                pass.WriteDepth(cameraDepth);
                pass.WriteTexture(oceanRenderResult, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(velocity);

                pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);
                pass.ReadTexture("_LengthToRoughness", lengthToRoughness);

                commonPassData.SetInputs(pass);
                pass.AddRenderPassData<OceanFftResult>();

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);

                    pass.SetInt(command, "_VerticesPerEdge", VerticesPerTileEdge);
                    pass.SetInt(command, "_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
                    pass.SetFloat(command, "_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));
                    pass.SetInt(command, "_OceanTextureSlicePreviousOffset", ((renderGraph.FrameIndex & 1) == 0) ? 0 : 4);

                    // Snap to quad-sized increments on largest cell
                    var texelSize = settings.Size / (float)settings.PatchVertices;
                    var positionX = MathUtils.Snap(camera.transform.position.x - settings.Size * 0.5f, texelSize) - camera.transform.position.x;
                    var positionZ = MathUtils.Snap(camera.transform.position.z - settings.Size * 0.5f, texelSize) - camera.transform.position.z;
                    pass.SetVector(command, "_PatchScaleOffset", new Vector4(settings.Size / (float)settings.CellCount, settings.Size / (float)settings.CellCount, positionX, positionZ));

                    var oceanScale = new Vector4(1f / patchSizes.x, 1f / patchSizes.y, 1f / patchSizes.z, 1f / patchSizes.w);
                    pass.SetVector(command, "_OceanScale", oceanScale);
                    pass.SetVector(command, "_RcpCascadeScales", rcpScales);
                    pass.SetVector(command, "_OceanTexelSize", texelSizes);

                    pass.SetInt(command, "_CullingPlanesCount", 6);
                    pass.SetVectorArray(command, "_CullingPlanes", cullingPlanes);
                });
            }

            return oceanRenderResult;
        }

        public RTHandle RenderUnderwaterLighting(int screenWidth, int screenHeight, RTHandle underwaterDepth, RTHandle cameraDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, IRenderPassData commonPassData, Camera camera)
        {
            var underwaterResultId = renderGraph.GetTexture(screenWidth, screenHeight, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ocean Underwater Lighting"))
            {
                pass.Initialize(underwaterLightingMaterial, camera: camera);
                pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(underwaterResultId, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Depth", underwaterDepth);
                pass.ReadTexture("_AlbedoMetallic", albedoMetallic);
                pass.ReadTexture("_NormalRoughness", normalRoughness);
                pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);
                pass.ReadTexture("_Emissive", emissive);

                pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<VolumetricLighting.Result>();
                pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<LitData.Result>();
                pass.AddRenderPassData<WaterShadowResult>();

                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    pass.SetVector(command, "_WaterExtinction", settings.Material.GetColor("_Extinction"));
                });
            }

            return underwaterResultId;
        }

        public void RenderDeferredWater(CullingResults cullingResults, RTHandle underwaterLighting, RTHandle waterNormalMask, RTHandle underwaterDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, RTHandle cameraDepth, IRenderPassData commonPassData, Camera camera)
        {
            // Calculate constants
            var rcpScales = new Vector4(1f / Mathf.Pow(settings.Profile.CascadeScale, 0f), 1f / Mathf.Pow(settings.Profile.CascadeScale, 1f), 1f / Mathf.Pow(settings.Profile.CascadeScale, 2f), 1f / Mathf.Pow(settings.Profile.CascadeScale, 3f));
            var patchSizes = new Vector4(settings.Profile.PatchSize / Mathf.Pow(settings.Profile.CascadeScale, 0f), settings.Profile.PatchSize / Mathf.Pow(settings.Profile.CascadeScale, 1f), settings.Profile.PatchSize / Mathf.Pow(settings.Profile.CascadeScale, 2f), settings.Profile.PatchSize / Mathf.Pow(settings.Profile.CascadeScale, 3f));
            var spectrumStart = new Vector4(0, settings.Profile.MaxWaveNumber * patchSizes.y / patchSizes.x, settings.Profile.MaxWaveNumber * patchSizes.z / patchSizes.y, settings.Profile.MaxWaveNumber * patchSizes.w / patchSizes.z);
            var spectrumEnd = new Vector4(settings.Profile.MaxWaveNumber, settings.Profile.MaxWaveNumber, settings.Profile.MaxWaveNumber, settings.Resolution);
            var oceanScale = new Vector4(1f / patchSizes.x, 1f / patchSizes.y, 1f / patchSizes.z, 1f / patchSizes.w);
            var rcpTexelSizes = new Vector4(settings.Resolution / patchSizes.x, settings.Resolution / patchSizes.y, settings.Resolution / patchSizes.z, settings.Resolution / patchSizes.w);
            var texelSizes = patchSizes / settings.Resolution;

            // Find first 2 directional lights
            Vector3 lightDirection0 = Vector3.up, lightDirection1 = Vector3.up;
            Color lightColor0 = Color.clear, lightColor1 = Color.clear;
            var dirLightCount = 0;
            for (var i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                var light = cullingResults.visibleLights[i];
                if (light.lightType != LightType.Directional)
                    continue;

                dirLightCount++;

                if (dirLightCount == 1)
                {
                    lightDirection0 = -light.localToWorldMatrix.Forward();
                    lightColor0 = light.finalColor;
                }
                else if (dirLightCount == 2)
                {
                    lightDirection1 = -light.localToWorldMatrix.Forward();
                    lightColor1 = light.finalColor;
                }
                else
                {
                    // Only 2 lights supported
                    break;
                }
            }

            var keyword = dirLightCount == 2 ? "LIGHT_COUNT_TWO" : (dirLightCount == 1 ? "LIGHT_COUNT_ONE" : string.Empty);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Water"))
            {
                pass.Initialize(deferredWaterMaterial, keyword: keyword, camera: camera);
                pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(albedoMetallic);
                pass.WriteTexture(normalRoughness);
                pass.WriteTexture(bentNormalOcclusion);
                pass.WriteTexture(emissive);

                pass.ReadTexture("_UnderwaterResult", underwaterLighting);
                pass.ReadTexture("_WaterNormalFoam", waterNormalMask);
                pass.ReadTexture("_UnderwaterDepth", underwaterDepth);
                pass.ReadTexture("_Depth", cameraDepth, subElement: RenderTextureSubElement.Depth);
                pass.ReadTexture("_Stencil", cameraDepth, subElement: RenderTextureSubElement.Stencil);

                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<WaterShadowResult>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<LitData.Result>();
                pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();

                commonPassData.SetInputs(pass);
                pass.AddRenderPassData<OceanFftResult>();

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);

                    var material = settings.Material;
                    pass.SetVector(command, "_Color", material.GetColor("_Color").linear);
                    pass.SetVector(command, "_Extinction", material.GetColor("_Extinction"));

                    pass.SetVector(command, "_UnderwaterResultScaleLimit", underwaterLighting.ScaleLimit2D);

                    pass.SetVector(command, "_LightDirection0", lightDirection0);
                    pass.SetVector(command, "_LightColor0", lightColor0);

                    pass.SetVector(command, "_LightDirection1", lightDirection1);
                    pass.SetVector(command, "_LightColor1", lightColor1);

                    pass.SetFloat(command, "_RefractOffset", material.GetFloat("_RefractOffset"));
                    pass.SetFloat(command, "_Steps", material.GetFloat("_Steps"));

                    pass.SetVector(command, "_OceanScale", oceanScale);

                    pass.SetVector(command, "_RcpCascadeScales", rcpScales);

                    pass.SetFloat(command, "_WaveFoamStrength", settings.Material.GetFloat("_WaveFoamStrength"));
                    pass.SetFloat(command, "_WaveFoamFalloff", settings.Material.GetFloat("_WaveFoamFalloff"));
                    pass.SetFloat(command, "_FoamNormalScale", settings.Material.GetFloat("_FoamNormalScale"));
                    pass.SetFloat(command, "_FoamSmoothness", settings.Material.GetFloat("_FoamSmoothness"));

                    var foamScale = settings.Material.GetTextureScale("_FoamTex");
                    var foamOffset = settings.Material.GetTextureOffset("_FoamTex");

                    pass.SetVector(command, "_FoamTex_ST", new Vector4(foamScale.x, foamScale.y, foamOffset.x, foamOffset.y));
                    pass.SetFloat(command, "_OceanGravity", settings.Profile.Gravity);
                    pass.SetTexture(command, "_LengthToRoughness", lengthToRoughness);

                    pass.SetTexture(command, "_FoamTex", settings.Material.GetTexture("_FoamTex"));
                    pass.SetTexture(command, "_FoamBump", settings.Material.GetTexture("_FoamBump"));

                });
            }
        }

        class PassData
        {

        }
    }

    public struct WaterShadowResult : IRenderPassData
    {
        private readonly RTHandle waterShadowTexture;
        private readonly Matrix4x4 waterShadowMatrix;
        private readonly float waterShadowNear, waterShadowFar;
        private readonly Vector3 waterShadowExtinction;

        public WaterShadowResult(RTHandle waterShadowTexture, Matrix4x4 waterShadowMatrix, float waterShadowNear, float waterShadowFar, Vector3 waterShadowExtinction)
        {
            this.waterShadowTexture = waterShadowTexture ?? throw new ArgumentNullException(nameof(waterShadowTexture));
            this.waterShadowMatrix = waterShadowMatrix;
            this.waterShadowNear = waterShadowNear;
            this.waterShadowFar = waterShadowFar;
            this.waterShadowExtinction = waterShadowExtinction;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("_WaterShadows", waterShadowTexture);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetMatrix(command, "_WaterShadowMatrix1", waterShadowMatrix);
            pass.SetFloat(command, "_WaterShadowNear", waterShadowNear);
            pass.SetFloat(command, "_WaterShadowFar", waterShadowFar);
            pass.SetVector(command, "_WaterShadowExtinction", waterShadowExtinction);
        }
    }

    public struct WaterCullResult
    {
        public BufferHandle IndirectArgsBuffer { get; }
        public BufferHandle PatchDataBuffer { get; }

        public WaterCullResult(BufferHandle indirectArgsBuffer, BufferHandle patchDataBuffer)
        {
            IndirectArgsBuffer = indirectArgsBuffer ?? throw new ArgumentNullException(nameof(indirectArgsBuffer));
            PatchDataBuffer = patchDataBuffer ?? throw new ArgumentNullException(nameof(patchDataBuffer));
        }
    }

    public struct WaterShadowCullResult : IRenderPassData
    {
        public BufferHandle IndirectArgsBuffer { get; }
        public BufferHandle PatchDataBuffer { get; }
        public float Near { get; }
        public float Far { get; }
        public Matrix4x4 WorldToClip { get; }
        public Matrix4x4 ShadowMatrix { get; }

        public WaterShadowCullResult(BufferHandle indirectArgsBuffer, BufferHandle patchDataBuffer, float near, float far, Matrix4x4 worldToClip, Matrix4x4 shadowMatrix)
        {
            IndirectArgsBuffer = indirectArgsBuffer ?? throw new ArgumentNullException(nameof(indirectArgsBuffer));
            PatchDataBuffer = patchDataBuffer ?? throw new ArgumentNullException(nameof(patchDataBuffer));
            Near = near;
            Far = far;
            WorldToClip = worldToClip;
            ShadowMatrix = shadowMatrix;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadBuffer("_PatchData", PatchDataBuffer);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }

    public struct WaterRenderCullResult : IRenderPassData
    {
        public BufferHandle IndirectArgsBuffer { get; }
        public BufferHandle PatchDataBuffer { get; }

        public WaterRenderCullResult(BufferHandle indirectArgsBuffer, BufferHandle patchDataBuffer)
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

    public struct OceanFftResult : IRenderPassData
    {
        public RTHandle OceanDisplacement { get; }
        public RTHandle OceanDisplacementHistory { get; }
        public RTHandle OceanNormalFoamSmoothness { get; }

        public OceanFftResult(RTHandle oceanDisplacement, RTHandle oceanDisplacementHistory, RTHandle oceanNormalFoamSmoothness)
        {
            OceanDisplacement = oceanDisplacement ?? throw new ArgumentNullException(nameof(oceanDisplacement));
            OceanDisplacementHistory = oceanDisplacementHistory ?? throw new ArgumentNullException(nameof(oceanDisplacementHistory));
            OceanNormalFoamSmoothness = oceanNormalFoamSmoothness ?? throw new ArgumentNullException(nameof(oceanNormalFoamSmoothness));
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("OceanDisplacement", OceanDisplacement);
            pass.ReadTexture("OceanDisplacementHistory", OceanDisplacementHistory);
            pass.ReadTexture("OceanNormalFoamSmoothness", OceanNormalFoamSmoothness);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}