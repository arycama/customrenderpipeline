using System;
using System.Collections.Generic;
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

        private Settings settings;
        private Material material;

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

            var pass = renderGraph.AddRenderPass<FullscreenRenderPass>();
            pass.SetRenderFunction((command, context) =>
            {
                // Downsample
                for (var i = 0; i < mipCount; i++)
                {
                    if (i == 0)
                    {
                        pass.SetTexture(command, "_MainTex", input);
                    }
                    else
                    {
                        var inputId = bloomIds[i - 1];
                        pass.SetTexture(command, "_MainTex", inputId);
                    }

                    var width = Mathf.Max(1, camera.pixelWidth >> (i + 1));
                    var height = Mathf.Max(1, camera.pixelHeight >> (i + 1));
                    pass.SetVector(command, "_RcpResolution", new Vector2(1.0f / width, 1.0f / height));

                    command.SetRenderTarget(bloomIds[i]);
                    command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, pass.GetPropertyBlock());
                }

                // Upsample
                for (var i = mipCount - 1; i > 0; i--)
                {
                    pass.SetFloat(command, "_Strength", settings.Strength);
                    pass.SetTexture(command, "_MainTex", bloomIds[i]);

                    var width = Mathf.Max(1, camera.pixelWidth >> i);
                    var height = Mathf.Max(1, camera.pixelHeight >> i);
                    pass.SetVector(command, "_RcpResolution", new Vector2(1.0f / width, 1.0f / height));

                    command.SetRenderTarget(i == 0 ? input : bloomIds[i - 1]);
                    command.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3, 1, pass.GetPropertyBlock());
                }

                ListPool<RTHandle>.Release(bloomIds);
            });

            var result = bloomIds[1];
            return result;
        }
    }
}