﻿using Arycama.CustomRenderPipeline;
using System;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct SkyTransmittanceData : IRenderPassData
{
    private readonly ResourceHandle<RenderTexture> skyTransmittance;
    private readonly int width, height;

    public SkyTransmittanceData(ResourceHandle<RenderTexture> skyTransmittance, int width, int height)
    {
        this.skyTransmittance = skyTransmittance;
        this.width = width;
        this.height = height;
    }

    public void SetInputs(RenderPass pass)
    {
        pass.ReadTexture("_SkyTransmittance", skyTransmittance);
    }

    public void SetProperties(RenderPass pass, CommandBuffer command)
    {
        pass.SetFloat("_TransmittanceWidth", width);
        pass.SetFloat("_TransmittanceHeight", height);
    }
}