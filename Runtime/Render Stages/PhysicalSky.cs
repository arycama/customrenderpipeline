using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class PhysicalSky
    {
        /// <summary>
        /// List of look at matrices for cubemap faces.
        /// Ref: https://msdn.microsoft.com/en-us/library/windows/desktop/bb204881(v=vs.85).aspx
        /// </summary>
        static public readonly Vector3[] lookAtList =
        {
            new Vector3(1.0f, 0.0f, 0.0f),
            new Vector3(-1.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, -1.0f, 0.0f),
            new Vector3(0.0f, 0.0f, 1.0f),
            new Vector3(0.0f, 0.0f, -1.0f),
        };

        /// <summary>
        /// List of up vectors for cubemap faces.
        /// Ref: https://msdn.microsoft.com/en-us/library/windows/desktop/bb204881(v=vs.85).aspx
        /// </summary>
        static public readonly Vector3[] upVectorList =
        {
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, 0.0f, -1.0f),
            new Vector3(0.0f, 0.0f, 1.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
        };

        [Serializable]
        public class Settings
        {
            [Header("Atmosphere Properties")]
            [SerializeField] private Vector3 rayleighScatter = new Vector3(5.802e-6f, 13.558e-6f, 33.1e-6f);
            [SerializeField] private float rayleighHeight = 8000.0f;
            [SerializeField] private float mieScatter = 3.996e-6f;
            [SerializeField] private float mieAbsorption = 4.4e-6f;
            [SerializeField] private float mieHeight = 1200.0f;
            [SerializeField, Range(-1.0f, 1.0f)] private float miePhase = 0.8f;
            [SerializeField] private Vector3 ozoneAbsorption = new Vector3(0.65e-6f, 1.881e-6f, 0.085e-6f);
            [SerializeField] private float ozoneWidth = 15000.0f;
            [SerializeField] private float ozoneHeight = 25000.0f;
            [SerializeField] private float planetRadius = 6360000.0f;
            [SerializeField] private float atmosphereThickness = 100000.0f;
            [SerializeField] private Color groundColor = Color.grey;

            [Header("Transmittance Lookup")]
            [SerializeField] private int transmittanceWidth = 128;
            [SerializeField] private int transmittanceHeight = 64;
            [SerializeField] private int transmittanceSamples = 64;

            [Header("Multi Scatter Lookup")]
            [SerializeField] private int multiScatterWidth = 32;
            [SerializeField] private int multiScatterHeight = 32;
            [SerializeField] private int multiScatterSamples = 64;

            [Header("Reflection Probe")]
            [SerializeField] private int reflectionResolution = 128;
            [SerializeField] private int reflectionSamples = 16;

            [Header("Rendering")]
            [SerializeField] private int renderSamples = 32;

            public Vector3 RayleighScatter => rayleighScatter;
            public float RayleighHeight => rayleighHeight;
            public float MieScatter => mieScatter;
            public float MieAbsorption => mieAbsorption;
            public float MieHeight => mieHeight;
            public float MiePhase => miePhase;
            public Vector3 OzoneAbsorption => ozoneAbsorption;
            public float OzoneWidth => ozoneWidth;
            public float OzoneHeight => ozoneHeight;
            public float PlanetRadius => planetRadius;
            public float AtmosphereThickness => atmosphereThickness;
            public Color GroundColor => groundColor;

            public int TransmittanceWidth => transmittanceWidth;
            public int TransmittanceHeight => transmittanceHeight;
            public int TransmittanceSamples => transmittanceSamples;

            public int MultiScatterWidth => multiScatterWidth;
            public int MultiScatterHeight => multiScatterHeight;
            public int MultiScatterSamples => multiScatterSamples;

            public int ReflectionResolution => reflectionResolution;
            public int ReflectionSamples => reflectionSamples;

            public int RenderSamples => renderSamples;
        }

        private RenderGraph renderGraph;
        private Settings settings;
        private Material material;

        public PhysicalSky(RenderGraph renderGraph, Settings settings)
        {
            this.renderGraph = renderGraph;
            this.settings = settings;

            material = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public IRenderPassData GenerateData(Vector3 viewPosition, LightingSetup.Result lightingSetupResult, BufferHandle exposureBuffer)
        {
            // Generate transmittance LUT
            var transmittance = renderGraph.GetTexture(settings.TransmittanceWidth, settings.TransmittanceHeight, GraphicsFormat.B10G11R11_UFloatPack32);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Atmosphere Transmittance"))
            {
                pass.Initialize(material);
                pass.WriteTexture(transmittance);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    pass.SetInt(command, "_Samples", settings.TransmittanceSamples);
                    pass.SetVector(command, "_ScaleOffset", new Vector4(1.0f / (settings.TransmittanceWidth - 1.0f), 1.0f / (settings.TransmittanceHeight - 1.0f), -0.5f / (settings.TransmittanceWidth - 1.0f), -0.5f / (settings.TransmittanceHeight - 1.0f)));
                });
            }

            // Generate multi-scatter LUT
            var multiScatter = renderGraph.GetTexture(settings.MultiScatterWidth, settings.MultiScatterHeight, GraphicsFormat.B10G11R11_UFloatPack32, true);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Atmosphere Multi Scatter"))
            {
                pass.Initialize(Resources.Load<ComputeShader>("PhysicalSky"), 0, settings.MultiScatterWidth, settings.MultiScatterHeight, 1, false);

                pass.ReadTexture("_Transmittance", transmittance);
                pass.WriteTexture("_MultiScatterResult", multiScatter);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    pass.SetVector(command, "_GroundColor", settings.GroundColor.linear);
                    pass.SetVector(command, "_AtmosphereTransmittanceRemap", GraphicsUtilities.HalfTexelRemap(settings.TransmittanceWidth, settings.TransmittanceHeight));
                    pass.SetInt(command, "_Samples", settings.MultiScatterSamples);
                    pass.SetVector(command, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(settings.MultiScatterWidth, settings.MultiScatterHeight));
                });
            }

            var transmittanceRemap = GraphicsUtilities.HalfTexelRemap(settings.TransmittanceWidth, settings.TransmittanceHeight);
            var multiScatterRemap = GraphicsUtilities.HalfTexelRemap(settings.MultiScatterWidth, settings.MultiScatterHeight);

            // Generate Reflection probe
            var skyReflection = renderGraph.GetTexture(settings.ReflectionResolution, settings.ReflectionResolution, GraphicsFormat.B10G11R11_UFloatPack32, dimension: TextureDimension.Cube, hasMips: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Sky Reflection"))
            {
                pass.Initialize(material, 1);
                pass.WriteTexture(skyReflection);
                pass.DepthSlice = RenderTargetIdentifier.AllDepthSlices;

                lightingSetupResult.SetInputs(pass);

                pass.ReadTexture("_Transmittance", transmittance);
                pass.ReadTexture("_MultiScatter", multiScatter);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    lightingSetupResult.SetProperties(pass, command);

                    pass.SetInt(command, "_Samples", settings.RenderSamples);
                    pass.SetVector(command, "_ViewPosition", viewPosition);
                    pass.SetConstantBuffer(command, "Exposure", exposureBuffer);

                    var array = ArrayPool<Matrix4x4>.Get(6);

                    for (var i = 0; i < 6; i++)
                    {
                        var rotation = Quaternion.LookRotation(lookAtList[i], upVectorList[i]);
                        var viewToWorld = Matrix4x4.TRS(viewPosition, rotation, Vector3.one);
                        array[i] = Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(settings.ReflectionResolution, settings.ReflectionResolution, Vector2.zero, 90.0f, 1.0f, viewToWorld, true);
                    }

                    ArrayPool<Matrix4x4>.Release(array);

                    pass.SetMatrixArray(command, "_PixelToWorldViewDirs", array);
                    pass.SetVector(command, "_AtmosphereTransmittanceRemap", transmittanceRemap);
                    pass.SetVector(command, "_MultiScatterRemap", multiScatterRemap);
                });
            }

            // Generate ambient probe
            var ambientBufferTemp = renderGraph.GetBuffer(7, sizeof(float) * 4, GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Atmosphere Ambient Probe"))
            {
                pass.Initialize(Resources.Load<ComputeShader>("PhysicalSky"), 1, 1, 1, 1, false);

                // Prefiltered importance sampling
                // Use lower MIP-map levels for fetching samples with low probabilities
                // in order to reduce the variance.
                // Ref: http://http.developer.nvidia.com/GPUGems3/gpugems3_ch20.html
                //
                // - OmegaS: Solid angle associated with the sample
                // - OmegaP: Solid angle associated with the texel of the cubemap
                var sampleCount = 256; // Must match PhysicalSky.compute
                var rcpOmegaP = 6.0f * settings.ReflectionResolution * settings.ReflectionResolution / (4.0f * Mathf.PI);
                var pdf = 1.0f / (4.0f * Mathf.PI);
                var omegaS = 1.0f / sampleCount / pdf;
                var mipLevel = 0.5f * Mathf.Log(omegaS * rcpOmegaP, 2.0f);

                pass.ReadTexture("_AmbientProbeInputCubemap", skyReflection);
                pass.WriteBuffer("_AmbientProbeOutputBuffer", ambientBufferTemp);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    pass.SetFloat(command, "_MipLevel", mipLevel);
                });
            }

            // Copy ambient
            var ambientBuffer= renderGraph.GetBuffer(7, sizeof(float) * 4, GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination);
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Atmosphere Ambient Probe Copy"))
            {
                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    command.CopyBuffer(ambientBufferTemp, ambientBuffer);
                });
            }

            // Specular convolution
            return new Result(transmittance, multiScatter, ambientBuffer, skyReflection, transmittanceRemap, multiScatterRemap);
        }

        public void Render(RTHandle target, RTHandle depth, BufferHandle exposureBuffer, int width, int height, float fov, float aspect, Matrix4x4 viewToWorld, Vector3 viewPosition, LightingSetup.Result lightingSetupResult, IRenderPassData atmosphereData, Vector2 jitter)
        {
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky"))
            {
                pass.Initialize(material, 2);
                pass.WriteTexture(target);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);

                lightingSetupResult.SetInputs(pass);
                atmosphereData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    lightingSetupResult.SetProperties(pass, command);
                    atmosphereData.SetProperties(pass, command);

                    pass.SetInt(command, "_Samples", settings.RenderSamples);
                    pass.SetVector(command, "_ViewPosition", viewPosition);
                    pass.SetConstantBuffer(command, "Exposure", exposureBuffer);
                    pass.SetMatrix(command, "_PixelToWorldViewDir", Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(width, height, jitter, fov, aspect, viewToWorld));
                });
            }
        }

        public void Cleanup()
        {
        }

        private class PassData
        {
        }

        public struct Result : IRenderPassData
        {
            public RTHandle Transmittance { get; }
            public RTHandle MultiScatter { get; }
            public RTHandle ReflectionProbe { get; }
            public BufferHandle AmbientBuffer { get; }

            public Vector4 TransmittanceRemap { get; }
            public Vector4 MultiScatterRemap { get; }

            public Result(RTHandle transmittance, RTHandle multiScatter, BufferHandle ambientBuffer, RTHandle reflectionProbe, Vector4 transmittanceRemap, Vector4 multiScatterRemap)
            {
                Transmittance = transmittance;
                MultiScatter = multiScatter;
                AmbientBuffer = ambientBuffer;
                ReflectionProbe = reflectionProbe;
                TransmittanceRemap = transmittanceRemap;
                MultiScatterRemap = multiScatterRemap;
            }

            void IRenderPassData.SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_Transmittance", Transmittance);
                pass.ReadTexture("_MultiScatter", MultiScatter);
                pass.ReadTexture("_SkyReflection", ReflectionProbe);
                pass.ReadBuffer("AmbientSh", AmbientBuffer);
            }

            void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetVector(command, "_AtmosphereTransmittanceRemap", TransmittanceRemap);
                pass.SetVector(command, "_MultiScatterRemap", MultiScatterRemap);
            }
        }
    }
}
