using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class OceanSystem
    {
        public class Settings
        {
            [field: SerializeField, Tooltip("The resolution of the simulation, higher numbers give more detail but are more expensive")] public int Resolution { get; private set; } = 128;
            [field: SerializeField, Tooltip("Use Trilinear for the normal/foam map, improves quality of lighting/reflections in shader")] public bool UseTrilinear { get; private set; } = true;
            [field: SerializeField, Range(1, 16), Tooltip("Anisotropic level for the normal/foam map")] public int AnisoLevel { get; private set; } = 4;
            [field: SerializeField] public Material Material { get; private set; }
            [field: SerializeField] public WaterProfile Profile { get; private set; }
            [field: SerializeField] public float ShadowRadius { get; private set; } = 8192;
            [field: SerializeField] public int ShadowResolution { get; private set; } = 512;
        }

        private static readonly IndexedShaderPropertyId smoothnessMapIds = new("SmoothnessOutput");

        private RenderTexture normalMap, foamSmoothness, DisplacementMap;
        private readonly Dictionary<Camera, bool> flips = new();
        private RenderTexture lengthToRoughness;
        private Settings settings;
        private RenderGraph renderGraph;

        public OceanSystem(RenderGraph renderGraph)
        {
            this.renderGraph = renderGraph;

            var resolution = settings.Resolution;
            var anisoLevel = settings.AnisoLevel;
            var useTrilinear = settings.UseTrilinear;

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
        }

        public void UpdateFft(Camera camera)
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
            var computeShader = Resources.Load<ComputeShader>("Ocean FFT");

            if (!flips.TryGetValue(camera, out var flip))
            {
                flips.Add(camera, false);
            }
            else
            {
                flip = !flip;
                flips[camera] = flip;
            }

            // Set Vectors
            //using var scope = context.ScopedCommandBuffer("Ocean", true);
            // GraphicsUtilities.SetupCameraProperties(scope.Command, FrameCount, camera, context, camera.Resolution(), out var viewProjectionMatrix);

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

                    command.SetGlobalInt("_OceanTextureSliceOffset", flip ? 4 : 0);
                    command.SetGlobalInt("_OceanTextureSlicePreviousOffset", flip ? 0 : 4);

                    command.SetComputeVectorParam(computeShader, "SpectrumStart", spectrumStart);
                    command.SetComputeVectorParam(computeShader, "SpectrumEnd", spectrumEnd);
                    command.SetComputeVectorParam(computeShader, "_RcpCascadeScales", rcpScales);
                    command.SetComputeVectorParam(computeShader, "_CascadeTexelSizes", texelSizes);

                    // Get Textures
                    command.SetComputeFloatParam(computeShader, "Smoothness", settings.Material.GetFloat("_Smoothness"));
                    command.SetComputeFloatParam(computeShader, "Time", Time.timeSinceLevelLoad);
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

        public void RenderShadow(ScriptableRenderContext context, Camera camera, CullingResults cullingResults)
        {
            var shadowResolution = settings.ShadowResolution;
            var shadowRadius = settings.ShadowRadius;
            var profile = settings.Profile;

            // Render
            var waterShadowId = renderGraph.GetTexture(shadowResolution, shadowResolution, GraphicsFormat.D32_SFloat);

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Ocean Cull"))
            {
                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    for (var i = 0; i < cullingResults.visibleLights.Length; i++)
                    {
                        var visibleLight = cullingResults.visibleLights[i];
                        if (visibleLight.lightType != LightType.Directional)
                            continue;

                        var size = new Vector3(shadowRadius * 2, profile.MaxWaterHeight * 2, shadowRadius * 2);
                        var min = new Vector3(camera.transform.position.x - shadowRadius, -profile.MaxWaterHeight, camera.transform.position.z - shadowRadius);

                        var localMatrix = Matrix4x4.Rotate(Quaternion.Inverse(visibleLight.light.transform.rotation));
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
                        var localSize = localMax - localMin;
                        var worldUnitsPerTexel = new Vector2(localSize.x, localSize.y) / shadowResolution;
                        localMin.x = Mathf.Floor(localMin.x / worldUnitsPerTexel.x) * worldUnitsPerTexel.x;
                        localMin.y = Mathf.Floor(localMin.y / worldUnitsPerTexel.y) * worldUnitsPerTexel.y;
                        localMax.x = Mathf.Floor(localMax.x / worldUnitsPerTexel.x) * worldUnitsPerTexel.x;
                        localMax.y = Mathf.Floor(localMax.y / worldUnitsPerTexel.y) * worldUnitsPerTexel.y;
                        localSize = localMax - localMin;

                        var localCenter = (localMax + localMin) * 0.5f;
                        var worldMatrix = Matrix4x4.Rotate(visibleLight.light.transform.rotation);
                        var position = worldMatrix.MultiplyPoint(new Vector3(localCenter.x, localCenter.y, localMin.z));

                        var lookMatrix = Matrix4x4.LookAt(position, position + visibleLight.light.transform.forward, visibleLight.light.transform.up);

                        // Matrix that mirrors along Z axis, to match the camera space convention.
                        var scaleMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1));
                        // Final view matrix is inverse of the LookAt matrix, and then mirrored along Z.
                        var viewMatrix = scaleMatrix * lookMatrix.inverse;

                        var projection = Matrix4x4.Ortho(-localSize.x * 0.5f, localSize.x * 0.5f, -localSize.y * 0.5f, localSize.y * 0.5f, 0, localSize.z);
                        //lhsProj.SetColumn(2, -lhsProj.GetColumn(2));

                        command.SetGlobalMatrix("_WaterShadowMatrix", GL.GetGPUProjectionMatrix(projection, true) * viewMatrix);
                        command.SetGlobalFloat("_WaterShadowNear", 0f);
                        command.SetGlobalFloat("_WaterShadowFar", localSize.z);
                        //command.SetGlobalDepthBias(constantBias, slopeBias);

                        command.SetRenderTarget(waterShadowId);
                        command.ClearRenderTarget(true, false, new Color());

                        var planes = ArrayPool<Plane>.Get(6);
                        GeometryUtility.CalculateFrustumPlanes(projection * viewMatrix, planes);

                        var cullingPlanes = ArrayPool<Vector4>.Get(6);
                        for (var j = 0; j < 6; j++)
                        {
                            var plane = planes[j];
                            cullingPlanes[j] = new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
                        }

                        ArrayPool<Plane>.Release(planes);

                        foreach (var waterRenderer in WaterRenderer.WaterRenderers)
                        {
                            waterRenderer.Cull(command, camera.transform.position, cullingPlanes);
                            waterRenderer.Render(command, "WaterShadow", camera.transform.position);
                        }

                        ArrayPool<Vector4>.Release(cullingPlanes);

                        command.SetGlobalMatrix("_WaterShadowMatrix", (projection * viewMatrix).ConvertToAtlasMatrix());

                        // Only render 1 light
                        break;
                    }

                    command.SetRenderTarget(BuiltinRenderTextureType.None);
                });
            }
        }

        public void CullWater(Camera camera, Vector4[] cullingPlanes)
        {
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Ocean Cull"))
            {
                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    foreach (var waterRenderer in WaterRenderer.WaterRenderers)
                        waterRenderer.Cull(command, camera.transform.position, cullingPlanes);
                });
            }
        }

        public void RenderWater(Camera camera)
        {
            // Depth, rgba8 normalFoam, rgba8 roughness, mask? 

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Ocean Render"))
            {
                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    foreach (var waterRenderer in WaterRenderer.WaterRenderers)
                        waterRenderer.Render(command, "Water", camera.transform.position);
                });
            }
        }

        public void RenderUnderwaterLighting()
        {

        }

        class PassData
        {

        }
    }
}