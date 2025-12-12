using UnityEngine;
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

    public ViewRenderData(Int2 viewSize, float near, float far, Float2 tanHalfFov, RigidTransform transform, Camera camera, ScriptableRenderContext context, ScriptableCullingParameters cullingParameters)
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
    }

    void IRenderPassData.SetInputs(RenderPass pass)
    {
    }

    void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
    {
    }
}