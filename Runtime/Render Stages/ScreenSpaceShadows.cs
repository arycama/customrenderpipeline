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
        [field: SerializeField] public bool UseRaytracing { get; private set; } = true;
        [field: SerializeField, Range(0.0f, 180.0f)] public float LightAngularDiameter { get; private set; } = 0.5f;
        [field: SerializeField, Range(0.0f, 1.0f)] public float Intensity { get; private set; } = 1.0f;
        [field: SerializeField, Range(1, 128)] public int MaxSamples { get; private set; } = 32;
        [field: SerializeField, Range(0f, 1.0f)] public float Thickness { get; private set; } = 0.1f;
    }

    private readonly RenderGraph renderGraph;
    private readonly Material material;
    private readonly Settings settings;
    private readonly RayTracingShader shadowRaytracingShader;

    public ScreenSpaceShadows(RenderGraph renderGraph, Settings settings)
    {
        this.renderGraph = renderGraph;
        material = new Material(Shader.Find("Hidden/ScreenSpaceShadows")) { hideFlags = HideFlags.HideAndDontSave };
        this.settings = settings;
        shadowRaytracingShader = Resources.Load<RayTracingShader>("Raytracing/Shadow");
    }

    public void Render(RTHandle depth, RTHandle hiZDepth, int width, int height, ICommonPassData commonPassData, Camera camera, CullingResults cullingResults, float bias, float distantBias, RTHandle normalRoughness)
    {
        var lightDirection = Vector3.up;
        for (var i = 0; i < cullingResults.visibleLights.Length; i++)
        {
            var light = cullingResults.visibleLights[i];
            if (light.lightType != LightType.Directional)
                continue;

            lightDirection = -light.localToWorldMatrix.Forward();
            break;
        }

        var result = renderGraph.GetTexture(width, height, GraphicsFormat.R8_UNorm);

        if (settings.UseRaytracing)
        {
            using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Raytraced Shadows"))
            {
                var raytracingData = renderGraph.ResourceMap.GetRenderPassData<RaytracingResult>(renderGraph.FrameIndex);

                pass.Initialize(shadowRaytracingShader, "RayGeneration", "RaytracingVisibility", raytracingData.Rtas, width, height, 1, bias, distantBias);
                pass.WriteTexture(result, "HitResult");
                pass.ReadTexture("_Depth", depth);

                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);
                    pass.SetVector(command, "LightDirection", lightDirection);
                    pass.SetFloat(command, "LightCosTheta", Mathf.Cos(settings.LightAngularDiameter * Mathf.Deg2Rad * 0.5f));
                });
            }
        }
        else
        {
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Shadows"))
            {
                pass.Initialize(material, 0, 1, null, camera);
                pass.WriteTexture(result);

                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<TemporalAA.TemporalAAData>();

                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_HiZDepth", hiZDepth);
                pass.ReadTexture("_NormalRoughness", normalRoughness);

                commonPassData.SetInputs(pass);
                var data = pass.SetRenderFunction<Data>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);
                    pass.SetVector(command, "LightDirection", lightDirection);
                    pass.SetFloat(command, "_MaxSteps", settings.MaxSamples);
                    pass.SetFloat(command, "_Thickness", settings.Thickness);
                    pass.SetFloat(command, "_Intensity", settings.Intensity);
                    pass.SetFloat(command, "_MaxMip", Texture2DExtensions.MipCount(width, height) - 1);
                    pass.SetFloat(command, "LightCosTheta", Mathf.Cos(settings.LightAngularDiameter * Mathf.Deg2Rad * 0.5f));
                });
            }
        }

        renderGraph.ResourceMap.SetRenderPassData(new Result(result), renderGraph.FrameIndex);
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
