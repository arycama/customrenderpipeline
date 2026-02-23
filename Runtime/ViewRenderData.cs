using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary> Contains data for a single view inside a rendering loop </summary>
public readonly struct ViewRenderData : IRenderPassData
{
    public readonly Int2 viewSize;
    public readonly float near, far;
    public readonly Float2 tanHalfFov;
    public readonly RigidTransform transform;
    public readonly int viewId;
    public readonly Camera camera;
    public readonly ScriptableRenderContext context;
    public readonly ScriptableCullingParameters cullingParameters;
    public readonly RenderTargetIdentifier target;
    public readonly VRTextureUsage vrTextureUsage;
    public readonly SinglePassStereoMode stereoMode;
    public readonly int instanceMultiplier;
    public readonly string stereoKeyword;
    public readonly GraphicsFormat format;
    public readonly int viewCount;
    public readonly bool requiresMirrorBlit;
    public readonly IntPtr? foveatedRenderingInfo;

    public ViewRenderData(Int2 viewSize, float near, float far, Float2 tanHalfFov, RigidTransform transform, Camera camera, ScriptableRenderContext context, ScriptableCullingParameters cullingParameters, RenderTargetIdentifier target, VRTextureUsage vrTextureUsage, SinglePassStereoMode stereoMode, int instanceMultiplier, string stereoKeyword, GraphicsFormat format, int viewCount, bool requiresMirrorBlit, IntPtr? foveatedRenderingInfo = null)
    {
        this.viewSize = viewSize;
        this.near = near;
        this.far = far;
        this.tanHalfFov = tanHalfFov;
        this.transform = transform;
        viewId = camera.GetHashCode();
        this.camera = camera;
        this.context = context;
        this.cullingParameters = cullingParameters;
        this.target = target;
        this.vrTextureUsage = vrTextureUsage;
        this.stereoMode = stereoMode;
        this.instanceMultiplier = instanceMultiplier;
        this.stereoKeyword = stereoKeyword;
        this.format = format;
        this.viewCount = viewCount;
        this.requiresMirrorBlit = requiresMirrorBlit;
        this.foveatedRenderingInfo = foveatedRenderingInfo;
    }

    void IRenderPassData.SetInputs(RenderPass pass)
    {
    }

    void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
    {
    }
}