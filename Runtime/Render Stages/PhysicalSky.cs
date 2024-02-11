using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class PhysicalSky
    {
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

            [SerializeField] private int multiScatterWidth = 32;
            [SerializeField] private int multiScatterHeight = 32;
            [SerializeField] private int multiScatterSamples = 64;

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

        public IRenderPassData GenerateData()
        {
            var transmittance = renderGraph.GetTexture(settings.TransmittanceWidth, settings.TransmittanceHeight, GraphicsFormat.B10G11R11_UFloatPack32);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky"))
            {
                pass.Material = material;
                pass.Index = 0;

                pass.WriteTexture("", transmittance, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    pass.SetInt(command, "_Samples", settings.TransmittanceSamples);
                    pass.SetVector(command, "_ScaleOffset", new Vector4(1.0f / (settings.TransmittanceWidth - 1.0f), 1.0f / (settings.TransmittanceHeight - 1.0f), -0.5f / (settings.TransmittanceWidth - 1.0f), -0.5f / (settings.TransmittanceHeight - 1.0f)));
                });
            }

            var multiScatter = renderGraph.GetTexture(settings.MultiScatterWidth, settings.MultiScatterHeight, GraphicsFormat.B10G11R11_UFloatPack32, true);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Physical Sky"))
            {
                pass.Initialize(Resources.Load<ComputeShader>("PhysicalSky"), 0, settings.MultiScatterWidth, settings.MultiScatterHeight, 1, false);

                pass.ReadTexture("_Transmittance", transmittance);
                pass.WriteTexture("_MultiScatterResult", multiScatter, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    pass.SetVector(command, "_GroundColor", settings.GroundColor.linear);
                    pass.SetVector(command, "_AtmosphereTransmittanceRemap", GraphicsUtilities.HalfTexelRemap(settings.TransmittanceWidth, settings.TransmittanceHeight));
                    pass.SetInt(command, "_Samples", settings.MultiScatterSamples);
                    pass.SetVector(command, "_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(settings.MultiScatterWidth, settings.MultiScatterHeight));
                });
            }

            return new Result(transmittance, multiScatter, GraphicsUtilities.HalfTexelRemap(settings.TransmittanceWidth, settings.TransmittanceHeight), GraphicsUtilities.HalfTexelRemap(settings.MultiScatterWidth, settings.MultiScatterHeight));
        }

        public void Render(RTHandle target, RTHandle depth, BufferHandle exposureBuffer, int width, int height, float fov, float aspect, Matrix4x4 viewToWorld, Vector3 viewPosition, LightingSetup.Result lightingSetupResult, IRenderPassData atmosphereData)
        {
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky"))
            {
                pass.Material = material;
                pass.Index = 1;

                pass.WriteTexture("", target, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                pass.WriteDepth("", depth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, flags: RenderTargetFlags.ReadOnlyDepthStencil);

                lightingSetupResult.SetInputs(pass);
                atmosphereData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    lightingSetupResult.SetProperties(pass, command);
                    atmosphereData.SetProperties(pass, command);

                    pass.SetVector(command, "_ViewPosition", viewPosition);
                    pass.SetConstantBuffer(command, "Exposure", exposureBuffer);
                    pass.SetMatrix(command, "_PixelCoordToViewDirWS", Matrix4x4Extensions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(width, height, fov, aspect, viewToWorld));
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
            //public BufferHandle AmbientBuffer { get; }

            public Vector4 TransmittanceRemap { get; }
            public Vector4 MultiScatterRemap { get; }

            public Result(RTHandle transmittance, RTHandle multiScatter, Vector4 transmittanceRemap, Vector4 multiScatterRemap)
            {
                Transmittance = transmittance;
                MultiScatter = multiScatter;
                TransmittanceRemap = transmittanceRemap;
                MultiScatterRemap = multiScatterRemap;
            }

            void IRenderPassData.SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_Transmittance", Transmittance);
                pass.ReadTexture("_MultiScatter", MultiScatter);
            }

            void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetVector(command, "_AtmosphereTransmittanceRemap", TransmittanceRemap);
                pass.SetVector(command, "_MultiScatterRemap", MultiScatterRemap);
            }
        }
    }
}
