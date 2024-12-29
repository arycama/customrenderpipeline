using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class Bloom : RenderFeature
    {
        [Serializable]
        public class Settings
        {
            [SerializeField, Range(0f, 1f)] private float strength = 0.125f;
            [SerializeField, Range(2, 12)] private int maxMips = 6;

            public float Strength => strength;
            public int MaxMips => maxMips;
        }

        private readonly Settings settings;
        private readonly Material material;

        public Bloom(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new(Shader.Find("Hidden/Bloom")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public override void Render()
        {
            var bloomIds = ListPool<ResourceHandle<RenderTexture>>.Get();
            var viewData = renderGraph.GetResource<ViewData>();

            // Need to queue up all the textures first
            var mipCount = Mathf.Min(settings.MaxMips, (int)Mathf.Log(Mathf.Max(viewData.PixelWidth, viewData.PixelHeight), 2));
            for (var i = 0; i < mipCount; i++)
            {
                var width = Mathf.Max(1, viewData.PixelWidth >> (i + 1));
                var height = Mathf.Max(1, viewData.PixelHeight >> (i + 1));

                var resultId = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32);
                bloomIds.Add(resultId);
            }

            // Downsample
            for (var i = 0; i < mipCount; i++)
            {

                using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Bloom");
                pass.Initialize(material, i == 0 ? 0 : 1);
                pass.WriteTexture(bloomIds[i], RenderBufferLoadAction.DontCare);

                var rt = i > 0 ? bloomIds[i - 1] : renderGraph.GetResource<CameraTargetData>().Handle;
                pass.ReadTexture("_Input", rt);

                var width = Mathf.Max(1, viewData.PixelWidth >> (i + 1));
                var height = Mathf.Max(1, viewData.PixelHeight >> (i + 1));

                pass.SetRenderFunction((new Vector2(1.0f / width, 1.0f / height), rt), (command, pass, data) =>
                {
                    pass.SetVector("_RcpResolution", data.Item1);
                    pass.SetVector("_InputScaleLimit", pass.GetScaleLimit2D(rt));
                });
            }

            // Upsample
            for (var i = mipCount - 1; i > 0; i--)
            {
                var input = bloomIds[i];

                using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Bloom");
                pass.Initialize(material, 2);
                pass.WriteTexture(bloomIds[i - 1]);
                pass.ReadTexture("_Input", input);

                var width = Mathf.Max(1, viewData.PixelWidth >> i);
                var height = Mathf.Max(1, viewData.PixelHeight >> i);

                pass.SetRenderFunction((settings.Strength, new Vector2(1f / width, 1f / height)), (command, pass, data) =>
                {
                    pass.SetFloat("_Strength", data.Strength);
                    pass.SetVector("_RcpResolution", data.Item2);
                    pass.SetVector("_InputScaleLimit", pass.GetScaleLimit2D(input));
                });
            }

            var result = bloomIds[0];
            ListPool<ResourceHandle<RenderTexture>>.Release(bloomIds);

            renderGraph.SetResource<BloomData>(new BloomData(result)); ;
        }
    }
}