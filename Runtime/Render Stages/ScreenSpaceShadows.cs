using Arycama.CustomRenderPipeline;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ScreenSpaceShadows
{
    [Serializable]
    public class Settings
    {
        [field: SerializeField, Range(0.0f, 1.0f)] public float Intensity { get; private set; } = 1.0f;
        [field: SerializeField, Range(1, 128)] public int MaxSamples { get; private set; } = 32;
        [field: SerializeField, Range(0f, 1.0f)] public float Thickness { get; private set; } = 0.1f;
    }

    private readonly RenderGraph renderGraph;
    private readonly Material material;
    private readonly Settings settings;

    public ScreenSpaceShadows(RenderGraph renderGraph, Settings settings)
    {
        this.renderGraph = renderGraph;
        material = new Material(Shader.Find("Hidden/ScreenSpaceShadows")) { hideFlags = HideFlags.HideAndDontSave };
        this.settings = settings;
    }

    public void Render(RTHandle depth, RTHandle hiZDepth, int width, int height, ICommonPassData commonPassData, Camera camera, CullingResults cullingResults)
    {
        var result = renderGraph.GetTexture(width, height, GraphicsFormat.R8_UNorm);

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Shadows"))
        {
            pass.Initialize(material, 0, 1, null, camera);
            pass.WriteTexture(result);

            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<ShadowRenderer.Result>();
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();

            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("_HiZDepth", hiZDepth);
            
            commonPassData.SetInputs(pass);

            var lightDirection = Vector3.up;
            for (var i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                var light = cullingResults.visibleLights[i];
                if (light.lightType != LightType.Directional)
                    continue;

                lightDirection = -light.localToWorldMatrix.Forward();
                break;
            }

            var data = pass.SetRenderFunction<Data>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
                pass.SetVector(command, "LightDirection", lightDirection);
                pass.SetFloat(command, "_MaxSteps", settings.MaxSamples);
                pass.SetFloat(command, "_Thickness", settings.Thickness);
                pass.SetFloat(command, "_Intensity", settings.Intensity);
            });
        }

        renderGraph.ResourceMap.SetRenderPassData(new Result(result));
    }

    private class Data
    {
    }

    public class Result : IRenderPassData
    {
        public RTHandle ScreenSpaceShadows { get; }

        public Result(RTHandle screenSpaceShadows)
        {
            ScreenSpaceShadows = screenSpaceShadows;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("ScreenSpaceShadows", ScreenSpaceShadows);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector(command, "ScreenSpaceShadowsScaleLimit", ScreenSpaceShadows.ScaleLimit2D);
        }
    }
}
