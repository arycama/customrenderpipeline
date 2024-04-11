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

        private RenderTexture normalMap, foamSmoothness, DisplacementMap;
        private RenderTexture lengthToRoughness;
        private Settings settings;
        private RenderGraph renderGraph;
        private Material underwaterLightingMaterial, deferredWaterMaterial;
        private GraphicsBuffer indexBuffer;

        private int VerticesPerTileEdge => settings.PatchVertices + 1;
        private int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;

        public OceanSystem(RenderGraph renderGraph, Settings settings)
        {
            this.renderGraph = renderGraph;
            this.settings = settings;

            var resolution = settings.Resolution;
            var anisoLevel = settings.AnisoLevel;
            var useTrilinear = settings.UseTrilinear;

            underwaterLightingMaterial = new Material(Shader.Find("Hidden/Underwater Lighting 1")) { hideFlags = HideFlags.HideAndDontSave };
            deferredWaterMaterial = new Material(Shader.Find("Hidden/Deferred Water 1")) { hideFlags = HideFlags.HideAndDontSave };

            // Initialize textures
            normalMap = new RenderTexture(resolution, resolution, 0, GraphicsFormat.R8G8_SNorm)
            {
                anisoLevel = anisoLevel,
                autoGenerateMips = false,
                dimension = TextureDimension.Tex2DArray,
                enableRandomWrite = true,
                filterMode = useTrilinear ? FilterMode.Trilinear : FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
                name = "Ocean Normal Map",
                useMipMap = true,
                volumeDepth = 8,
                wrapMode = TextureWrapMode.Repeat,
            }.Created();

            // Initialize textures
            foamSmoothness = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                anisoLevel = anisoLevel,
                autoGenerateMips = false,
                dimension = TextureDimension.Tex2DArray,
                enableRandomWrite = true,
                filterMode = useTrilinear ? FilterMode.Trilinear : FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
                name = "Ocean Normal Map",
                useMipMap = true,
                volumeDepth = 8,
                wrapMode = TextureWrapMode.Repeat,
            }.Created();

            DisplacementMap = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf)
            {
                anisoLevel = anisoLevel,
                autoGenerateMips = false,
                dimension = TextureDimension.Tex2DArray,
                enableRandomWrite = true,
                filterMode = useTrilinear ? FilterMode.Trilinear : FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
                name = "Ocean Displacement",
                useMipMap = true,
                volumeDepth = 8,
                wrapMode = TextureWrapMode.Repeat,
            }.Created();

            lengthToRoughness = new RenderTexture(256, 1, 0, RenderTextureFormat.R16)
            {
                enableRandomWrite = true,
                hideFlags = HideFlags.HideAndDontSave,
                name = "Length to Smoothness",
            }.Created();

            // First pass will shorten normal based on the average normal length from the smoothness
            var computeShader = Resources.Load<ComputeShader>("Utility/SmoothnessFilter");
            var generateLengthToSmoothnessKernel = computeShader.FindKernel("GenerateLengthToSmoothness");
            computeShader.SetFloat("_MaxIterations", 32);
            computeShader.SetFloat("_Resolution", 256);
            computeShader.SetTexture(generateLengthToSmoothnessKernel, "_LengthToRoughnessResult", lengthToRoughness);
            computeShader.DispatchNormalized(generateLengthToSmoothnessKernel, 256, 1, 1);

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

        public void UpdateFft()
        {
            var resolution = settings.Resolution;
            var anisoLevel = settings.AnisoLevel;
            var useTrilinear = settings.UseTrilinear;
            var profile = settings.Profile;

            //if (anisoLevel != DisplacementMap.anisoLevel)
            //{
            //Cleanup();
            //Initialize();
            //}

            // Simulate
            var tempBufferID4 = Shader.PropertyToID("TempFFTBuffer4");
            var fftNormalBufferId = Shader.PropertyToID("_FFTNormalBuffer");
            var tempBufferID2 = Shader.PropertyToID("TempFFTBuffer2");

            // Calculate constants
            var rcpScales = new Vector4(1f / Mathf.Pow(profile.CascadeScale, 0f), 1f / Mathf.Pow(profile.CascadeScale, 1f), 1f / Mathf.Pow(profile.CascadeScale, 2f), 1f / Mathf.Pow(profile.CascadeScale, 3f));
            var patchSizes = new Vector4(profile.PatchSize / Mathf.Pow(profile.CascadeScale, 0f), profile.PatchSize / Mathf.Pow(profile.CascadeScale, 1f), profile.PatchSize / Mathf.Pow(profile.CascadeScale, 2f), profile.PatchSize / Mathf.Pow(profile.CascadeScale, 3f));
            var spectrumStart = new Vector4(0, profile.MaxWaveNumber * patchSizes.y / patchSizes.x, profile.MaxWaveNumber * patchSizes.z / patchSizes.y, profile.MaxWaveNumber * patchSizes.w / patchSizes.z);
            var spectrumEnd = new Vector4(profile.MaxWaveNumber, profile.MaxWaveNumber, profile.MaxWaveNumber, resolution);
            var oceanScale = new Vector4(1f / patchSizes.x, 1f / patchSizes.y, 1f / patchSizes.z, 1f / patchSizes.w);
            var rcpTexelSizes = new Vector4(resolution / patchSizes.x, resolution / patchSizes.y, resolution / patchSizes.z, resolution / patchSizes.w);
            var texelSizes = patchSizes / resolution;

            // Load resources
            var computeShader = Resources.Load<ComputeShader>("OceanFFT");
            var oceanBuffer = profile.SetShaderProperties(renderGraph);

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Ocean Fft"))
            {
                pass.ReadBuffer("OceanData", oceanBuffer);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    command.SetGlobalVector("_OceanScale", oceanScale);
                    command.SetGlobalVector("_RcpCascadeScales", rcpScales);
                    command.SetGlobalVector("_OceanTexelSize", rcpTexelSizes);

                    command.SetGlobalFloat("_OceanGravity", profile.Gravity);

                    command.SetGlobalInt("_OceanTextureSliceOffset", ((renderGraph.FrameIndex & 1) == 0) ? 4 : 0);
                    command.SetGlobalInt("_OceanTextureSlicePreviousOffset", ((renderGraph.FrameIndex & 1) == 0) ? 0 : 4);

                    command.SetComputeVectorParam(computeShader, "SpectrumStart", spectrumStart);
                    command.SetComputeVectorParam(computeShader, "SpectrumEnd", spectrumEnd);
                    command.SetComputeVectorParam(computeShader, "_RcpCascadeScales", rcpScales);
                    command.SetComputeVectorParam(computeShader, "_CascadeTexelSizes", texelSizes);

                    // Get Textures
                    command.SetComputeFloatParam(computeShader, "Smoothness", settings.Material.GetFloat("_Smoothness"));
                    command.SetComputeFloatParam(computeShader, "Time", EditorApplication.isPlaying ? Time.time : (float)EditorApplication.timeSinceStartup);
                    command.GetTemporaryRTArray(tempBufferID4, resolution, resolution, 4, 0, FilterMode.Point, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear, 1, true);
                    command.GetTemporaryRTArray(fftNormalBufferId, resolution, resolution, 4, 0, FilterMode.Point, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear, 1, true);
                    command.GetTemporaryRTArray(tempBufferID2, resolution, resolution, 4, 0, FilterMode.Point, RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear, 1, true);

                    // FFT Row
                    command.SetComputeTextureParam(computeShader, 0, "targetTexture", tempBufferID4);
                    command.SetComputeTextureParam(computeShader, 0, "targetTexture1", tempBufferID2);
                    command.SetComputeTextureParam(computeShader, 0, "targetTexture2", fftNormalBufferId);
                    command.DispatchCompute(computeShader, 0, 1, resolution, 4);

                    command.SetComputeTextureParam(computeShader, 1, "sourceTexture", tempBufferID4);
                    command.SetComputeTextureParam(computeShader, 1, "sourceTexture1", tempBufferID2);
                    command.SetComputeTextureParam(computeShader, 1, "sourceTexture2", fftNormalBufferId);
                    command.SetComputeTextureParam(computeShader, 1, "DisplacementOutput", DisplacementMap);
                    command.SetComputeTextureParam(computeShader, 1, "NormalOutput", normalMap);
                    command.DispatchCompute(computeShader, 1, 1, resolution, 4);

                    command.SetComputeTextureParam(computeShader, 2, "DisplacementInput", DisplacementMap);
                    command.SetComputeTextureParam(computeShader, 2, "_NormalFoamSmoothness", foamSmoothness);
                    command.SetComputeTextureParam(computeShader, 2, "_NormalMap", normalMap);
                    command.DispatchNormalized(computeShader, 2, resolution, resolution, 4);

                    // Foam
                    command.SetComputeFloatParam(computeShader, "_FoamStrength", profile.FoamStrength);
                    command.SetComputeFloatParam(computeShader, "_FoamDecay", profile.FoamDecay);
                    command.SetComputeFloatParam(computeShader, "_FoamThreshold", profile.FoamThreshold);
                    command.SetComputeTextureParam(computeShader, 4, "_NormalFoamSmoothness", foamSmoothness);
                    command.SetComputeTextureParam(computeShader, 4, "_NormalMap", normalMap);
                    command.DispatchNormalized(computeShader, 4, resolution, resolution, 4);

                    // Release resources
                    command.ReleaseTemporaryRT(tempBufferID4);
                    command.ReleaseTemporaryRT(tempBufferID2);
                    command.ReleaseTemporaryRT(fftNormalBufferId);
                    command.GenerateMips(foamSmoothness);
                    command.GenerateMips(DisplacementMap);
                    command.GenerateMips(normalMap);

                    var generateMapsKernel = computeShader.FindKernel("GenerateMaps");
                    var mipCount = normalMap.mipmapCount;
                    command.SetComputeIntParam(computeShader, "Resolution", resolution >> 2);
                    command.SetComputeIntParam(computeShader, "Size", resolution >> 2);
                    command.SetGlobalTexture("_LengthToRoughness", lengthToRoughness);
                    command.SetComputeTextureParam(computeShader, generateMapsKernel, "_OceanNormalMap", normalMap);

                    for (var j = 0; j < mipCount; j++)
                    {
                        var smoothnessId = smoothnessMapIds.GetProperty(j);
                        command.SetComputeTextureParam(computeShader, generateMapsKernel, smoothnessId, foamSmoothness, j);
                    }

                    command.DispatchNormalized(computeShader, generateMapsKernel, (resolution * 4) >> 2, (resolution) >> 2, 1);

                    command.SetGlobalTexture("_OceanFoamSmoothnessMap", foamSmoothness);
                    command.SetGlobalTexture("_OceanNormalMap", normalMap);
                    command.SetGlobalTexture("_OceanDisplacementMap", DisplacementMap);

                });
            }
        }

        public void CullShadow(Camera camera, CullingResults cullingResults, ICommonPassData commonPassData)
        {
            var shadowResolution = settings.ShadowResolution;
            var shadowRadius = settings.ShadowRadius;
            var profile = settings.Profile;

            var lightRotation = Quaternion.identity;

            // Render
            Vector3 localSize = Vector3.zero;
            Matrix4x4 waterShadowMatrix = Matrix4x4.identity, projection = Matrix4x4.identity, viewMatrix = Matrix4x4.identity;
            for (var i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                var visibleLight = cullingResults.visibleLights[i];
                if (visibleLight.lightType != LightType.Directional)
                    continue;

                lightRotation = visibleLight.localToWorldMatrix.rotation;
                break;
            }

            var size = new Vector3(shadowRadius * 2, profile.MaxWaterHeight * 2, shadowRadius * 2);
            var min = new Vector3(camera.transform.position.x - shadowRadius, -profile.MaxWaterHeight, camera.transform.position.z - shadowRadius);

            var localMatrix = Matrix4x4.Rotate(Quaternion.Inverse(lightRotation));
            Vector3 localMin = Vector3.positiveInfinity, localMax = Vector3.negativeInfinity;

            for (var z = 0; z < 2; z++)
            {
                for (var y = 0; y < 2; y++)
                {
                    for (var x = 0; x < 2; x++)
                    {
                        var localPosition = localMatrix.MultiplyPoint(min + Vector3.Scale(size, new Vector3(x, y, z)));
                        localMin = Vector3.Min(localMin, localPosition);
                        localMax = Vector3.Max(localMax, localPosition);
                    }
                }
            }

            // Snap texels
            localSize = localMax - localMin;
            var worldUnitsPerTexel = new Vector2(localSize.x, localSize.y) / shadowResolution;
            localMin.x = Mathf.Floor(localMin.x / worldUnitsPerTexel.x) * worldUnitsPerTexel.x;
            localMin.y = Mathf.Floor(localMin.y / worldUnitsPerTexel.y) * worldUnitsPerTexel.y;
            localMax.x = Mathf.Floor(localMax.x / worldUnitsPerTexel.x) * worldUnitsPerTexel.x;
            localMax.y = Mathf.Floor(localMax.y / worldUnitsPerTexel.y) * worldUnitsPerTexel.y;
            localSize = localMax - localMin;

            var localCenter = (localMax + localMin) * 0.5f;
            var worldMatrix = Matrix4x4.Rotate(lightRotation);
            var position = worldMatrix.MultiplyPoint(new Vector3(localCenter.x, localCenter.y, localMin.z));

            var lookMatrix = Matrix4x4.LookAt(position, position + lightRotation.Forward(), lightRotation.Up());

            // Matrix that mirrors along Z axis, to match the camera space convention.
            var scaleMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1));
            // Final view matrix is inverse of the LookAt matrix, and then mirrored along Z.
            viewMatrix = scaleMatrix * lookMatrix.inverse;

            projection = Matrix4x4.Ortho(-localSize.x * 0.5f, localSize.x * 0.5f, -localSize.y * 0.5f, localSize.y * 0.5f, 0, localSize.z);
            //lhsProj.SetColumn(2, -lhsProj.GetColumn(2));

            var planes = ArrayPool<Plane>.Get(6);
            GeometryUtility.CalculateFrustumPlanes(projection * viewMatrix, planes);

            var cullingPlanes = ArrayPool<Vector4>.Get(6);
            for (var j = 0; j < 6; j++)
            {
                var plane = planes[j];
                cullingPlanes[j] = new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
            }
            ArrayPool<Plane>.Release(planes);

            var cullResult = Cull( camera.transform.position, cullingPlanes, commonPassData);
            renderGraph.ResourceMap.SetRenderPassData(new WaterShadowCullResult(cullResult.IndirectArgsBuffer, cullResult.PatchDataBuffer, 0.0f, localSize.z, viewMatrix, projection));
        }

        public void RenderShadow(Vector3 viewPosition)
        {
            var shadowResolution = settings.ShadowResolution;
            var waterShadowId = renderGraph.GetTexture(shadowResolution, shadowResolution, GraphicsFormat.D32_SFloat);

            var passIndex = settings.Material.FindPass("WaterShadow");
            Assert.IsTrue(passIndex != -1, "Water Material does not contain a Water Shadow Pass");

            var passData = renderGraph.ResourceMap.GetRenderPassData<WaterShadowCullResult>();
            using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Ocean Shadow"))
            {
                pass.Initialize(settings.Material, indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads);
                pass.WriteDepth(waterShadowId);
                pass.ConfigureClear(RTClearFlags.Depth);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    command.SetGlobalMatrix("_WaterShadowMatrix", GL.GetGPUProjectionMatrix(passData.Projection, true) * passData.View);

                    // TODO: use read buffer
                    command.SetGlobalBuffer("_PatchData", passData.PatchDataBuffer);

                    pass.SetInt(command, "_VerticesPerEdge", VerticesPerTileEdge);
                    pass.SetInt(command, "_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
                    pass.SetFloat(command, "_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));

                    // Snap to quad-sized increments on largest cell
                    var texelSize = settings.Size / (float)settings.PatchVertices;
                    var positionX = MathUtils.Snap(viewPosition.x - settings.Size * 0.5f, texelSize) - viewPosition.x;
                    var positionZ = MathUtils.Snap(viewPosition.z - settings.Size * 0.5f, texelSize) - viewPosition.z;
                    pass.SetVector(command, "_PatchScaleOffset", new Vector4(settings.Size / (float)settings.CellCount, settings.Size / (float)settings.CellCount, positionX, positionZ));
                });
            }

            var waterShadowMatrix = (passData.Projection * passData.View).ConvertToAtlasMatrix();
            renderGraph.ResourceMap.SetRenderPassData(new WaterShadowResult(waterShadowId, waterShadowMatrix, passData.Near, passData.Far));
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

                    var index = i;
                    var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                    {
                        // First pass sets the buffer contents
                        if(isFirstPass)
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

                        // Todo: Buffer handles
                        command.SetComputeBufferParam(compute, 0, "_IndirectArgs", indirectArgsBuffer);
                        command.SetComputeBufferParam(compute, 0, "_PatchDataWrite", patchDataBuffer);

                        // Do up to 6 passes per dispatch.
                        pass.SetInt(command, "_PassCount", passCount);
                        pass.SetInt(command, "_PassOffset", 6 * index);
                        pass.SetInt(command, "_TotalPassCount", totalPassCount);

                        pass.SetVectorArray(command, "_CullingPlanes", cullingPlanes);

                        // Snap to quad-sized increments on largest cell
                        var texelSizeX = settings.Size / (float)settings.PatchVertices;
                        var texelSizeZ = settings.Size / (float)settings.PatchVertices;
                        var positionX = Mathf.Floor((viewPosition.x - settings.Size * 0.5f) / texelSizeX) * texelSizeX;
                        var positionZ = Mathf.Floor((viewPosition.z - settings.Size * 0.5f) / texelSizeZ) * texelSizeZ;
                        var positionOffset = new Vector4(settings.Size, settings.Size, positionX, positionZ);
                        pass.SetVector(command, "_TerrainPositionOffset", positionOffset);

                        pass.SetFloat(command, "_EdgeLength", (float)settings.EdgeLength * settings.PatchVertices);
                        pass.SetInt(command, "_CullingPlanesCount", cullingPlanes.Length);

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

                    var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                    {
                        // If more than one pass needed, we need a second pass to write out lod deltas to the patch data
                        // Copy count from indirect draw args so we only dispatch as many threads as needed
                        command.SetComputeBufferParam(compute, 1, "_IndirectArgsInput", indirectArgsBuffer);
                    });
                }

                using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Ocean Quadtree Cull"))
                {
                    pass.Initialize(compute, lodIndirectArgsBuffer, 2);
                    pass.ReadTexture("_LodInput", tempLodId);

                    var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                    {
                        pass.SetInt(command, "_CellCount", settings.CellCount);
                        command.SetComputeBufferParam(compute, 2, "_PatchDataWrite", patchDataBuffer);
                        command.SetComputeBufferParam(compute, 2, "_IndirectArgs", indirectArgsBuffer);
                    });
                }
            }

            ListPool<RTHandle>.Release(tempIds);

            return new(indirectArgsBuffer, patchDataBuffer);
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
            public Matrix4x4 View { get; }
            public Matrix4x4 Projection { get; }

            public WaterShadowCullResult(BufferHandle indirectArgsBuffer, BufferHandle patchDataBuffer, float near, float far, Matrix4x4 view, Matrix4x4 projection)
            {
                IndirectArgsBuffer = indirectArgsBuffer ?? throw new ArgumentNullException(nameof(indirectArgsBuffer));
                PatchDataBuffer = patchDataBuffer ?? throw new ArgumentNullException(nameof(patchDataBuffer));
                Near = near;
                Far = far;
                View = view;
                Projection = projection;
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

        public void CullRender(Vector3 viewPosition, Vector4[] cullingPlanes, ICommonPassData commonPassData)
        {
            var result = Cull(viewPosition, cullingPlanes, commonPassData);
            renderGraph.ResourceMap.SetRenderPassData(new WaterRenderCullResult(result.IndirectArgsBuffer, result.PatchDataBuffer));
        }

        public RTHandle RenderWater(Camera camera, RTHandle cameraDepth, int screenWidth, int screenHeight, RTHandle velocity, IRenderPassData commonPassData)
        {
            // Depth, rgba8 normalFoam, rgba8 roughness, mask? 
            // Writes depth, stencil, and RGBA8 containing normalRG, roughness and foam
            var oceanRenderResult = renderGraph.GetTexture(screenWidth, screenHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true);

            var passIndex = settings.Material.FindPass("Water");
            Assert.IsTrue(passIndex != -1, "Water Material has no Water Pass");

            using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Ocean Render"))
            {
                var passData = renderGraph.ResourceMap.GetRenderPassData<WaterRenderCullResult>();
                pass.Initialize(settings.Material, indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);

                pass.WriteDepth(cameraDepth);
                pass.WriteTexture(oceanRenderResult, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(velocity);
                pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);

                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);

                    pass.SetInt(command, "_VerticesPerEdge", VerticesPerTileEdge);
                    pass.SetInt(command, "_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
                    pass.SetFloat(command, "_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));

                    // Snap to quad-sized increments on largest cell
                    var texelSize = settings.Size / (float)settings.PatchVertices;
                    var positionX = MathUtils.Snap(camera.transform.position.x - settings.Size * 0.5f, texelSize) - camera.transform.position.x;
                    var positionZ = MathUtils.Snap(camera.transform.position.z - settings.Size * 0.5f, texelSize) - camera.transform.position.z;
                    pass.SetVector(command, "_PatchScaleOffset", new Vector4(settings.Size / (float)settings.CellCount, settings.Size / (float)settings.CellCount, positionX, positionZ));
                });
            }

            return oceanRenderResult;
        }

        public RTHandle RenderUnderwaterLighting(int screenWidth, int screenHeight, RTHandle underwaterDepth, RTHandle cameraDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, IRenderPassData commonPassData)
        {
            var underwaterResultId = renderGraph.GetTexture(screenWidth, screenHeight, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ocean Underwater Lighting"))
            {
                pass.Initialize(underwaterLightingMaterial);
                pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(underwaterResultId, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Depth", cameraDepth);
                pass.ReadTexture("_UnderwaterDepth", underwaterDepth);
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

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    pass.SetVector(command, "_WaterExtinction", settings.Material.GetColor("_Extinction"));
                });
            }

            return underwaterResultId;
        }

        public void RenderDeferredWater(CullingResults cullingResults, RTHandle underwaterLighting, RTHandle waterNormalMask, RTHandle underwaterDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, RTHandle cameraDepth, IRenderPassData commonPassData)
        {
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
                pass.Initialize(deferredWaterMaterial, keyword: keyword);
                pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(albedoMetallic);
                pass.WriteTexture(normalRoughness);
                pass.WriteTexture(bentNormalOcclusion);
                pass.WriteTexture(emissive);

                pass.ReadTexture("_UnderwaterResult", underwaterLighting);
                pass.ReadTexture("_WaterNormalFoam", waterNormalMask);
                pass.ReadTexture("_UnderwaterDepth", underwaterDepth);
                pass.ReadTexture("_Depth", cameraDepth);

                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<WaterShadowResult>();
                pass.AddRenderPassData<LitData.Result>();

                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);

                    var material = settings.Material;
                    pass.SetVector(command, "_Color", material.GetColor("_Color").linear);
                    pass.SetVector(command, "_Extinction", material.GetColor("_Extinction").linear);

                    pass.SetVector(command, "_LightDirection0", lightDirection0);
                    pass.SetVector(command, "_LightColor0", lightColor0);

                    pass.SetVector(command, "_LightDirection1", lightDirection1);
                    pass.SetVector(command, "_LightColor1", lightColor1);

                    pass.SetFloat(command, "_RefractOffset", material.GetFloat("_RefractOffset"));
                    pass.SetFloat(command, "_Steps", material.GetFloat("_Steps"));
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

        public WaterShadowResult(RTHandle waterShadowTexture, Matrix4x4 waterShadowMatrix, float waterShadowNear, float waterShadowFar)
        {
            this.waterShadowTexture = waterShadowTexture ?? throw new ArgumentNullException(nameof(waterShadowTexture));
            this.waterShadowMatrix = waterShadowMatrix;
            this.waterShadowNear = waterShadowNear;
            this.waterShadowFar = waterShadowFar;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("_WaterShadows", waterShadowTexture);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetMatrix(command, "_WaterShadowMatrix", waterShadowMatrix);
            pass.SetFloat(command, "_WaterShadowNear", waterShadowNear);
            pass.SetFloat(command, "_WaterShadowFar", waterShadowFar);
        }
    }
}