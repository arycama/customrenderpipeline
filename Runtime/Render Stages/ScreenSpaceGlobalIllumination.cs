using Arycama.CustomRenderPipeline;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ScreenSpaceGlobalIllumination
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

    private PersistentRTHandleCache temporalCache;

    public ScreenSpaceGlobalIllumination(RenderGraph renderGraph, Settings settings)
    {
        this.renderGraph = renderGraph;
        material = new Material(Shader.Find("Hidden/ScreenSpaceGlobalIllumination")) { hideFlags = HideFlags.HideAndDontSave };
        this.settings = settings;

        temporalCache = new PersistentRTHandleCache(GraphicsFormat.B10G11R11_UFloatPack32, renderGraph, "Screen Space Reflections");
    }

    public void Render(RTHandle depth, int width, int height, ICommonPassData commonPassData, Camera camera, RTHandle previousFrame, RTHandle velocity, RTHandle normalRoughness, RTHandle hiZDepth, RTHandle bentNormalOcclusion)
    {
        var tempResult = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32);

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Trace"))
        {
            pass.Initialize(material, 0, 1, null, camera);
            pass.WriteTexture(tempResult);

            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();
            pass.AddRenderPassData<AutoExposure.AutoExposureData>();
            pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();

            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("PreviousFrame", previousFrame);
            pass.ReadTexture("Velocity", velocity);
            pass.ReadTexture("_NormalRoughness", normalRoughness);
            pass.ReadTexture("_HiZDepth", hiZDepth);
            pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);

            commonPassData.SetInputs(pass);

            var data = pass.SetRenderFunction<Data>((command, pass, data) =>
            {
                pass.SetFloat(command, "_Intensity", settings.Intensity);
                pass.SetFloat(command, "_MaxSteps", settings.MaxSamples);
                pass.SetFloat(command, "_Thickness", settings.Thickness);
                pass.SetVector(command, "_PreviousColorScaleLimit", previousFrame.ScaleLimit2D);
                commonPassData.SetProperties(pass, command);
            });
        }

        var (current, history, wasCreated) = temporalCache.GetTextures(width, height, camera, true);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Temporal"))
        {
            pass.Initialize(material, 1, camera: camera);
            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

            pass.ReadTexture("_Input", tempResult);
            pass.ReadTexture("_History", history);
            pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("Velocity", velocity);

            commonPassData.SetInputs(pass);
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();
            pass.AddRenderPassData<AutoExposure.AutoExposureData>();

            var data = pass.SetRenderFunction<Data>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
                pass.SetFloat(command, "_IsFirst", wasCreated ? 1.0f : 0.0f);
                pass.SetVector(command, "_HistoryScaleLimit", history.ScaleLimit2D);
            });
        }

        renderGraph.ResourceMap.SetRenderPassData(new Result(current));
    }

    private class Data
    {
    }

    public struct Result : IRenderPassData
    {
        public RTHandle ScreenSpaceGlobalIllumination { get; }

        public Result(RTHandle screenSpaceGlobalIllumination)
        {
            ScreenSpaceGlobalIllumination = screenSpaceGlobalIllumination;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("ScreenSpaceGlobalIllumination", ScreenSpaceGlobalIllumination);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector(command, "ScreenSpaceGlobalIlluminationScaleLimit", ScreenSpaceGlobalIllumination.ScaleLimit2D);
        }
    }
}
