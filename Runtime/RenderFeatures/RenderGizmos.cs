using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class RenderGizmos : ViewRenderFeature
{
    public RenderGizmos(RenderGraph renderGraph) : base(renderGraph)
    {
    }

    public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
    {
#if UNITY_EDITOR
        if (!UnityEditor.Handles.ShouldRenderGizmos())
            return;

        var preImageEffects = context.CreateGizmoRendererList(viewPassData.camera, GizmoSubset.PreImageEffects);
        var postImageEffects = context.CreateGizmoRendererList(viewPassData.camera, GizmoSubset.PostImageEffects);
        var wireOverlay = context.CreateWireOverlayRendererList(viewPassData.camera);
        var gizmosTarget = renderGraph.GetTexture(viewPassData.viewSize, GraphicsFormat.R8G8B8A8_SRGB, isScreenTexture: true);

        var size = renderGraph.RtHandleSystem.ScreenSize;
        //var viewport = new Rect(0, 0, viewPassData.viewSize.x, viewPassData.viewSize.y);
        var viewport = new Rect(0, size.y - viewPassData.viewSize.y, viewPassData.viewSize.x, viewPassData.viewSize.y);

        using var pass = renderGraph.AddGenericRenderPass("Render Gizmos", (preImageEffects, postImageEffects, wireOverlay, gizmosTarget, viewport));
        pass.WriteTexture(gizmosTarget);

        pass.SetRenderFunction(static (command, pass, data) =>
        {
            command.SetRenderTarget(pass.GetRenderTexture(data.gizmosTarget));
            command.ClearRenderTarget(RTClearFlags.Color, Color.clear);
            command.SetViewport(data.viewport);
            command.DrawRendererList(data.preImageEffects);
            command.DrawRendererList(data.postImageEffects);
            command.DrawRendererList(data.wireOverlay);
        });

        renderGraph.SetResource<GizmosTarget>(new(gizmosTarget));
#endif
    }
}