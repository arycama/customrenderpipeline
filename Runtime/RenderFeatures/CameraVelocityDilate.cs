using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    public class CameraVelocityDilate : ViewRenderFeature
    {
        private readonly Material material;
        private readonly TemporalAA.Settings setings;

        public CameraVelocityDilate(RenderGraph renderGraph, TemporalAA.Settings setings) : base(renderGraph)
        {
            material = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
            this.setings = setings;
        }

        public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
        {
            if (!setings.IsEnabled)
                return;

            var result = renderGraph.GetTexture(viewPassData.viewSize, GraphicsFormat.R16G16_SFloat);
            using (var pass = renderGraph.AddFullscreenRenderPass("Velocity Dilate"))
            {
                pass.Initialize(material, viewPassData.viewSize, viewPassData.viewCount, 1, isScreenPass: true);
                pass.WriteTexture(result);
                pass.ReadRtHandle<CameraVelocity>();
                pass.ReadRtHandle<CameraDepth>();
            }

            renderGraph.SetRTHandle<CameraVelocity>(result);
        }
    }
}