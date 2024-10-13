using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class BlitToScreen : RenderFeature
    {
        private readonly Material material;
        private readonly RenderGraph rendergraph;
        private readonly Tonemapping.Settings settings;

        public BlitToScreen(RenderGraph renderGraph, Tonemapping.Settings settings) : base(renderGraph)
        {
            material = new Material(Shader.Find("Hidden/Tonemapping")) { hideFlags = HideFlags.HideAndDontSave };
            this.settings = settings;
        }

        public void Render(RTHandle input, RTHandle uITexture, bool isSceneView, int width, int height)
        {
            using var pass = renderGraph.AddRenderPass<BlitToScreenPass>("Tonemapping");
            pass.Initialize(material, 1);
            pass.ReadTexture("_MainTex", input);
            pass.ReadTexture("UITexture", uITexture);
            pass.AddRenderPassData<AutoExposure.AutoExposureData>();

            var hdrSettings = HDROutputSettings.main;
            //var minNits = hdrSettings.available && settings.AutoDetectValues ? hdrSettings.minToneMapLuminance : settings.HdrMinNits;
            //var maxNits = hdrSettings.available && settings.AutoDetectValues ? hdrSettings.maxToneMapLuminance : settings.HdrMaxNits;
            //if (minNits < 0 || maxNits <= 0)
            //{
            //    minNits = settings.HdrMinNits;
            //    maxNits = settings.HdrMaxNits;
            //}
            var hdrEnabled = hdrSettings.available && settings.HdrEnabled;

            var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
            {
                pass.SetVector(command, "_Resolution", new Vector2(width, height));
                pass.SetFloat(command, "_IsSceneView", isSceneView ? 1.0f : 0.0f);
                var colorGamut = hdrEnabled ? hdrSettings.displayColorGamut : ColorGamut.sRGB;
                pass.SetInt(command, "ColorGamut", (int)colorGamut);
                pass.SetFloat(command, "HdrMaxNits", settings.Tonemap && hdrSettings.available && hdrSettings.active ? hdrSettings.maxToneMapLuminance : 100.0f);
            });
        }
    }
}
