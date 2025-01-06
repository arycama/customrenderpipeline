using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public partial class TemporalAA : RenderFeature
    {
        private readonly Settings settings;
        private readonly PersistentRTHandleCache textureCache;
        private readonly Material material;

        public TemporalAA(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Temporal AA")) { hideFlags = HideFlags.HideAndDontSave };
            textureCache = new(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Temporal AA");
        }

        protected override void Cleanup(bool disposing)
        {
            textureCache.Dispose();
        }

        public override void Render()
        {
            if (!settings.IsEnabled)
                return;

            var viewData = renderGraph.GetResource<ViewData>();
            var (current, history, wasCreated) = textureCache.GetTextures(viewData.PixelWidth, viewData.PixelHeight, viewData.ViewIndex);
            var result = renderGraph.GetTexture(viewData.PixelWidth, viewData.PixelHeight, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Temporal AA"))
            {
                var keyword = viewData.Scale < 1.0f ? "UPSCALE" : null;
                pass.Initialize(material, 0, 1, keyword);

                pass.ReadTexture("_History", history);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(result, RenderBufferLoadAction.DontCare);
                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<CameraTargetData>();
                pass.AddRenderPassData<VelocityData>();

                pass.SetRenderFunction((
                    spatialSharpness: settings.SpatialSharpness,
                    motionSharpness: settings.MotionSharpness * 0.8f,
                    hasHistory: wasCreated ? 0.0f : 1.0f,
                    stationaryBlending: settings.StationaryBlending,
                    motionBlending: settings.MotionBlending,
                    motionWeight: settings.MotionWeight,
                    scale: viewData.Scale,
                    maxWidth: viewData.ScaledWidth - 1,
                    maxHeight: viewData.ScaledHeight - 1,
                    maxResolution: new Vector2(viewData.PixelWidth - 1, viewData.PixelHeight - 1)
                ),
                (command, pass, data) =>
                {
                    pass.SetFloat("_SpatialSharpness", data.spatialSharpness);
                    pass.SetFloat("_MotionSharpness", data.motionSharpness);
                    pass.SetFloat("_HasHistory", data.hasHistory);
                    pass.SetFloat("_StationaryBlending", data.stationaryBlending);
                    pass.SetFloat("_VelocityBlending", data.motionBlending);
                    pass.SetFloat("_VelocityWeight", data.motionWeight);
                    pass.SetFloat("_Scale", data.scale);

                    pass.SetVector("_HistoryScaleLimit", pass.GetScaleLimit2D(history));

                    pass.SetVector("_MaxResolution", data.maxResolution);

                    pass.SetInt("_MaxWidth", data.maxWidth);
                    pass.SetInt("_MaxHeight", data.maxHeight);
                });

                renderGraph.SetResource(new CameraTargetData(result)); ;
            }
        }
    }
}