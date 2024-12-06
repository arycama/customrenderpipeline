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

        public RTHandle Render(Camera camera, RTHandle target)
        {
            var bloomIds = ListPool<RTHandle>.Get();

            // Need to queue up all the textures first
            var mipCount = Mathf.Min(settings.MaxMips, (int)Mathf.Log(Mathf.Max(camera.pixelWidth, camera.pixelHeight), 2));
            for (var i = 0; i < mipCount; i++)
            {
                var width = Mathf.Max(1, camera.pixelWidth >> (i + 1));
                var height = Mathf.Max(1, camera.pixelHeight >> (i + 1));

                var resultId = renderGraph.GetTexture(width, height, GraphicsFormat.A2B10G10R10_UNormPack32);
                bloomIds.Add(resultId);
            }

            // Downsample
            for (var i = 0; i < mipCount; i++)
            {
                var input = i == 0 ? target : bloomIds[i - 1];

                using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Bloom");
                pass.Initialize(material, i == 0 ? 0 : 1);
                pass.WriteTexture(bloomIds[i], RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_Input", input);

                var width = Mathf.Max(1, camera.pixelWidth >> (i + 1));
                var height = Mathf.Max(1, camera.pixelHeight >> (i + 1));

                pass.SetRenderFunction(new Vector2(1.0f / width, 1.0f / height), (command, pass, data) =>
                {
                    pass.SetVector(command, "_RcpResolution", data);
                    pass.SetVector(command, "_InputScaleLimit", input.ScaleLimit2D);
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

                var width = Mathf.Max(1, camera.pixelWidth >> i);
                var height = Mathf.Max(1, camera.pixelHeight >> i);

                pass.SetRenderFunction((settings.Strength, new Vector2(1f / width, 1f / height)), (command, pass, data) =>
                {
                    pass.SetFloat(command, "_Strength", data.Strength);
                    pass.SetVector(command, "_RcpResolution", data.Item2);
                    pass.SetVector(command, "_InputScaleLimit", input.ScaleLimit2D);
                });
            }

            var result = bloomIds[0];
            ListPool<RTHandle>.Release(bloomIds);
            return result;
        }
    }
}