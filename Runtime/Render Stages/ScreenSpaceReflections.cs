using Arycama.CustomRenderPipeline;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ScreenSpaceReflections
{
    [Serializable]
    public class Settings
    {
        [field: SerializeField, Range(0.0f, 1.0f)] public float Intensity { get; private set; } = 1.0f;
        [field: SerializeField, Range(1, 128)] public int MaxSamples { get; private set; } = 32;
        [field: SerializeField, Range(0f, 1.0f)] public float Thickness { get; private set; } = 0.1f;
    }

    private Material material;
    private RenderGraph renderGraph;
    private Settings settings;

    private PersistentRTHandleCache temporalCache;

    public ScreenSpaceReflections(RenderGraph renderGraph, Settings settings)
    {
        this.renderGraph = renderGraph;
        this.settings = settings;

        material = new Material(Shader.Find("Hidden/ScreenSpaceReflections")) { hideFlags = HideFlags.HideAndDontSave };
        temporalCache = new PersistentRTHandleCache(GraphicsFormat.B10G11R11_UFloatPack32, renderGraph, "Screen Space Reflections");
    }

    public void Render(RTHandle depth, RTHandle hiZDepth, RTHandle previousFrameColor, RTHandle normalRoughness, Camera camera, ICommonPassData commonPassData, int width, int height, RTHandle velocity, RTHandle bentNormalOcclusion)
    {
        // Must be screen texture since we use stencil to skip sky pixels
        var tempResult = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Reflections Trace"))
        {
            pass.Initialize(material, camera: camera);
            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(tempResult, RenderBufferLoadAction.DontCare);
            pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
            pass.ReadTexture("_NormalRoughness", normalRoughness);
            pass.ReadTexture("_PreviousColor", previousFrameColor);
            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("_HiZDepth", hiZDepth);
            pass.ReadTexture("Velocity", velocity);
            pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);

            commonPassData.SetInputs(pass);
            pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
            pass.AddRenderPassData<LitData.Result>();
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();
            pass.AddRenderPassData<AutoExposure.AutoExposureData>();

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                pass.SetFloat(command, "_Intensity", settings.Intensity);
                pass.SetFloat(command, "_MaxSteps", settings.MaxSamples);
                pass.SetFloat(command, "_Thickness", settings.Thickness);
                pass.SetVector(command, "_PreviousColorScaleLimit", previousFrameColor.ScaleLimit2D);
                commonPassData.SetProperties(pass, command);
            });
        }

        var (current, history, wasCreated) = temporalCache.GetTextures(width, height, camera, true);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Reflections Temporal"))
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

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
                pass.SetFloat(command, "_IsFirst", wasCreated ? 1.0f : 0.0f);
                pass.SetVector(command, "_HistoryScaleLimit", history.ScaleLimit2D);
            });
        }

        renderGraph.ResourceMap.SetRenderPassData(new ScreenSpaceReflectionResult(current));
    }

    class PassData
    {
    }
}

public struct ScreenSpaceReflectionResult : IRenderPassData
{
    public RTHandle ScreenSpaceReflections { get; }

    public ScreenSpaceReflectionResult(RTHandle screenSpaceReflections)
    {
        ScreenSpaceReflections = screenSpaceReflections ?? throw new ArgumentNullException(nameof(screenSpaceReflections));
    }

    public void SetInputs(RenderPass pass)
    {
        pass.ReadTexture("ScreenSpaceReflections", ScreenSpaceReflections);
    }

    public void SetProperties(RenderPass pass, CommandBuffer command)
    {
        pass.SetVector(command, "ScreenSpaceReflectionsScaleLimit", ScreenSpaceReflections.ScaleLimit2D);
    }
}