using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Unmath;
using static Unmath.Math;

namespace CustomRenderPipeline
{
    public class Bloom : ViewRenderFeature
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField, Range(0f, 1f)] public float Strength { get; private set; } = 0.125f;
            [field: SerializeField, Range(1, 12)] public int MaxMips { get; private set; } = 6;
        }

        private readonly Settings settings;
        private readonly Material material;

        public Bloom(RenderGraph renderGraph, Settings settings) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Bloom")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
        {
            if (settings.Strength == 0)
                return;

            renderGraph.AddProfileBeginPass("Bloom");

            var mipCount = Min(settings.MaxMips, (int)Log2(Max(viewPassData.viewSize.x, viewPassData.viewSize.y)));
            Span<ResourceHandle<RenderTexture>> bloomIds = stackalloc ResourceHandle<RenderTexture>[mipCount];

            // Downsample
            for (var i = 0; i < mipCount; i++)
            {
                var width = Max(1, viewPassData.viewSize.x >> (i + 1));
                var height = Max(1, viewPassData.viewSize.y >> (i + 1));

                var dest = renderGraph.GetTexture(new(width, height), GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
                bloomIds[i] = dest;

                var source = i > 0 ? bloomIds[i - 1] : renderGraph.GetRtHandleData<CameraTarget>().handle;

                using var pass = renderGraph.AddFullscreenRenderPass("Bloom Down", (1.0f / new Float2(width, height), source));

                pass.UseProfiler = false;

                pass.Initialize(material, new(width, height), viewPassData.viewCount, i == 0 ? 0 : 1, isScreenPass: true);
                pass.WriteTexture(dest);

                pass.ReadTexture("Input", source);
                pass.ReadResource<ViewData>();

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    pass.SetVector("RcpResolution", data.Item1);
                    pass.SetVector("InputScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.source));
                });
            }

            // Upsample
            for (var i = mipCount - 1; i > 0; i--)
            {
                var input = bloomIds[i];
                var width = Max(1, viewPassData.viewSize.x >> i);
                var height = Max(1, viewPassData.viewSize.y >> i);

                using var pass = renderGraph.AddFullscreenRenderPass("Bloom Up", (1.0f / new Float2(width, height), input, settings.Strength));
                pass.UseProfiler = false;

                pass.Initialize(material, new(width, height), viewPassData.viewCount, 2, isScreenPass: true);
                pass.WriteTexture(bloomIds[i - 1]);
                pass.ReadTexture("Input", input);

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    pass.SetFloat("Strength", data.Strength);
                    pass.SetVector("RcpResolution", data.Item1);
                    pass.SetVector("InputScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.Item2));
                });
            }

            renderGraph.SetRTHandle<CameraBloom>(bloomIds[0]);
            renderGraph.AddProfileEndPass("Bloom");
        }
    }
}