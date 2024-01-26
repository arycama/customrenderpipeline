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

        class Pass0Data
        {
            public Vector2 rcpResolution;
        }

        class Pass1Data
        {
            public float strength;
            public Vector2 rcpResolution;
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

                pass.Material = material;
                pass.Index = 0;
                pass.WriteTexture("", bloomIds[i], RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                pass.ReadTexture("_MainTex", i == 0 ? input : bloomIds[i - 1]);

                var width = Mathf.Max(1, camera.pixelWidth >> (i + 1));
                var height = Mathf.Max(1, camera.pixelHeight >> (i + 1));

                var data = pass.SetRenderFunction<Pass0Data>((command, context, pass, data) =>
                {
                    pass.SetVector(command, "_RcpResolution", data.rcpResolution);
                });

                data.rcpResolution = new Vector2(1.0f / width, 1.0f / height);
            }

            // Upsample
            for (var i = mipCount - 1; i > 0; i--)
            {
                using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>();

                pass.Material = material;
                pass.Index = 1;
                pass.WriteTexture("", bloomIds[i - 1], RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                pass.ReadTexture("_MainTex", bloomIds[i]);

                var width = Mathf.Max(1, camera.pixelWidth >> i);
                var height = Mathf.Max(1, camera.pixelHeight >> i);

                var data = pass.SetRenderFunction<Pass1Data>((command, context, pass, data) =>
                {
                    pass.SetFloat(command, "_Strength", data.strength);
                    pass.SetVector(command, "_RcpResolution", data.rcpResolution);
                });

                data.rcpResolution = new Vector2(1.0f / width, 1.0f / height);
            }

            var result = bloomIds[0];
            ListPool<RTHandle>.Release(bloomIds);
            return result;
        }
    }
}