using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;

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

            var pass0 = renderGraph.AddRenderPass<FullscreenRenderPass>();
            pass0.Initialize(material, 0);
            pass0.SetRenderFunction((command, context) =>
            {
                // Downsample
                for (var i = 0; i < mipCount; i++)
                {
                    if (i == 0)
                    {
                        pass0.SetTexture(command, "_MainTex", input);
                    }
                    else
                    {
                        var inputId = bloomIds[i - 1];
                        pass0.SetTexture(command, "_MainTex", inputId);
                    }

                    var width = Mathf.Max(1, camera.pixelWidth >> (i + 1));
                    var height = Mathf.Max(1, camera.pixelHeight >> (i + 1));
                    pass0.SetVector(command, "_RcpResolution", new Vector2(1.0f / width, 1.0f / height));

                    command.SetRenderTarget(bloomIds[i]);
                    pass0.Execute(command);
                }
            });

            var pass1 = renderGraph.AddRenderPass<FullscreenRenderPass>();
            pass1.Initialize(material, 1);
            pass1.SetRenderFunction((command, context) =>
            {
                // Upsample
                for (var i = mipCount - 1; i > 0; i--)
                {
                    pass1.SetFloat(command, "_Strength", settings.Strength);
                    pass1.SetTexture(command, "_MainTex", bloomIds[i]);

                    var width = Mathf.Max(1, camera.pixelWidth >> i);
                    var height = Mathf.Max(1, camera.pixelHeight >> i);
                    pass1.SetVector(command, "_RcpResolution", new Vector2(1.0f / width, 1.0f / height));

                    command.SetRenderTarget(i == 0 ? input : bloomIds[i - 1]);
                    pass1.Execute(command);
                }

                ListPool<RTHandle>.Release(bloomIds);
            });

            return bloomIds[1];
        }
    }
}