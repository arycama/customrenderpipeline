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
            [SerializeField, Range(2, 8)] private int maxMips = 6;

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

        public RTHandle Render(Camera camera, RTHandle input)
        {
            var bloomIds = ListPool<RTHandle>.Get();

            // Need to queue up all the textures first
            var mipCount = Mathf.Min(settings.MaxMips, (int)Mathf.Log(Mathf.Max(camera.pixelWidth, camera.pixelHeight), 2));
            for (var i = 0; i < mipCount; i++)
            {
                var width = Mathf.Max(1, camera.pixelWidth >> (i + 1));
                var height = Mathf.Max(1, camera.pixelHeight >> (i + 1));

                var resultId = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32);
                bloomIds.Add(resultId);
            }

            // Downsample
            for (var i = 0; i < mipCount; i++)
            {
                using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>();

                pass.RenderPass.Material = material;
                pass.RenderPass.Index = 0;
                pass.RenderPass.WriteTexture("", bloomIds[i], RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                pass.RenderPass.ReadTexture("_MainTex", i == 0 ? input : bloomIds[i - 1]);

                var width = Mathf.Max(1, camera.pixelWidth >> (i + 1));
                var height = Mathf.Max(1, camera.pixelHeight >> (i + 1));

                pass.RenderPass.SetRenderFunction((command, context) =>
                {
                    pass.RenderPass.SetVector(command, "_RcpResolution", new Vector2(1.0f / width, 1.0f / height));
                });
            }

            // Upsample
            for (var i = mipCount - 1; i > 0; i--)
            {
                using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>();

                pass.RenderPass.Material = material;
                pass.RenderPass.Index = 1;
                pass.RenderPass.WriteTexture("", bloomIds[i - 1], RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                pass.RenderPass.ReadTexture("_MainTex", bloomIds[i]);

                var width = Mathf.Max(1, camera.pixelWidth >> i);
                var height = Mathf.Max(1, camera.pixelHeight >> i);

                pass.RenderPass.SetRenderFunction((command, context) =>
                {
                    pass.RenderPass.SetFloat(command, "_Strength", settings.Strength);
                    pass.RenderPass.SetVector(command, "_RcpResolution", new Vector2(1.0f / width, 1.0f / height));
                });
            }

            var result = bloomIds[0];
            ListPool<RTHandle>.Release(bloomIds);
            return result;
        }
    }
}