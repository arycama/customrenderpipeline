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

        public override void Render()
        {
            using (var pass = renderGraph.AddRenderPass<BlitToScreenPass>("Tonemapping"))
            {
                pass.Initialize(material, 1);
                pass.AddRenderPassData<UITextureData>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<CameraTargetData>();

                var hdrSettings = HDROutputSettings.main;
                //var minNits = hdrSettings.available && settings.AutoDetectValues ? hdrSettings.minToneMapLuminance : settings.HdrMinNits;
                //var maxNits = hdrSettings.available && settings.AutoDetectValues ? hdrSettings.maxToneMapLuminance : settings.HdrMaxNits;
                //if (minNits < 0 || maxNits <= 0)
                //{
                //    minNits = settings.HdrMinNits;
                //    maxNits = settings.HdrMaxNits;
                //}
                var hdrEnabled = hdrSettings.available && settings.HdrEnabled;
                var maxNits = hdrEnabled ? hdrSettings.maxToneMapLuminance : 100.0f;
                var viewData = renderGraph.GetResource<ViewData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("_IsSceneView", viewData.Camera.cameraType == CameraType.SceneView ? 1.0f : 0.0f);
                    var colorGamut = hdrEnabled ? hdrSettings.displayColorGamut : ColorGamut.sRGB;
                    pass.SetInt("ColorGamut", (int)colorGamut);
                    pass.SetFloat("MaxLuminance", hdrEnabled ? maxNits : settings.PaperWhite);
                    pass.SetFloat("PaperWhiteLuminance", settings.PaperWhite); // Todo: Brightness setting
                });
            }
        }
    }
}
