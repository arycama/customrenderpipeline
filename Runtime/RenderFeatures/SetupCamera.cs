using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

public class SetupCamera : ViewRenderFeature
{
    private readonly Sky.Settings sky;
    private readonly Dictionary<int, (Float3, Quaternion, Float4x4)> previousCameraTransform = new();
    private readonly Dictionary<int, Float4> previousViewScaleLimit = new();
    private readonly Dictionary<int, double> previousTimeCache = new();

    public SetupCamera(RenderGraph renderGraph, Sky.Settings sky) : base(renderGraph)
    {
        this.sky = sky;
    }

    public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
    {
        var rawJitter = renderGraph.GetResource<TemporalAASetupData>().jitter;

        var jitter = 2.0f * rawJitter / (Float2)viewPassData.viewSize;

        var near = viewPassData.near;
        var far = viewPassData.far;
        var viewPosition = viewPassData.position;
        var viewRotation = viewPassData.rotation;

        // Matrices
        // Screen
        var screenToPixel = Float4x4.Scale(new Float3((Float2)viewPassData.viewSize, 1));
        var pixelToScreen = Float4x4.Scale(new Float3(1 / (Float2)viewPassData.viewSize, 1));

        // Clip
        var clipToScreen = Float4x4.ScaleOffset(new Float3(0.5f, -0.5f, 1), new Float2(0.5f, 0).xxy);
        var screenToClip = Float4x4.ScaleOffset(new Float3(2, -2, 1), new Float3(-1, 1, 0));
        var clipToPixel = screenToPixel.Mul(clipToScreen);
        var pixelToClip = screenToClip.Mul(pixelToScreen);

        // View
        var viewToNonJitteredClip = Float4x4.PerspectiveReverseZ(viewPassData.tanHalfFov, near, far);
        var nonJitteredClipToView = Float4x4.PerspectiveReverseZInverse(viewPassData.tanHalfFov, near, far);

        var viewToClip = viewToNonJitteredClip;
        viewToClip.m02 = jitter.x;
        viewToClip.m12 = jitter.y;
        var clipToView = nonJitteredClipToView;
        clipToView.m03 = viewPassData.tanHalfFov.x * jitter.x;
        clipToView.m13 = viewPassData.tanHalfFov.y * jitter.y;

        var viewToScreen = clipToScreen.Mul(viewToClip);
        var screenToView = clipToView.Mul(screenToClip);

        var viewToPixel = screenToPixel.Mul(viewToScreen);
        var pixelToView = clipToView.Mul(pixelToClip);

        var viewToWorld = Float4x4.Rotate(viewRotation);
        var worldToView = Float4x4.Rotate(viewRotation.Inverse);

        // World
        var worldToClip = viewToClip.Mul(worldToView);
        var clipToWorld = viewToWorld.Mul(clipToView);

        var worldToScreen = clipToScreen.Mul(worldToClip);
        var screenToWorld = viewToWorld.Mul(screenToView);

        var worldToPixel = screenToPixel.Mul(worldToScreen);
        var pixelToWorld = viewToWorld.Mul(pixelToView);

        // Previous frame matrices
        // Need to manually flip this since we're trying to match previous clip space but the renderer won't automatically flip since there's no viewport transform
        var viewToNonJitteredScreen = clipToScreen.Mul(viewToNonJitteredClip);
        if (!previousCameraTransform.TryGetValue(viewPassData.viewId, out var previousTransform))
            previousTransform = (viewPosition, viewRotation, viewToNonJitteredScreen);

        previousCameraTransform[viewPassData.viewId] = (viewPosition, viewRotation, viewToNonJitteredScreen);

        var worldToPreviousView = Float4x4.WorldToLocal(previousTransform.Item1 - viewPosition, previousTransform.Item2);
        var worldToPreviousScreen = previousTransform.Item3.Mul(worldToPreviousView);
        var pixelToWorldDir = Float4x4.PixelToWorldViewDirectionMatrix(viewPassData.viewSize, jitter, viewPassData.tanHalfFov, viewToWorld, true, false);

        // TODO: I think this is similar to pixel to world view dir matrix, maybe make a shared function
        var pixelToViewScaleOffset = new Float4
        (viewPassData.tanHalfFov.x * 2.0f / viewPassData.viewSize.x,
        -viewPassData.tanHalfFov.y * 2.0f / viewPassData.viewSize.y,
        -viewPassData.tanHalfFov.x * (1.0f - jitter.x),
        viewPassData.tanHalfFov.y * (1.0f - jitter.y));

        if (!previousTimeCache.TryGetValue(viewPassData.viewId, out var previousTime))
            previousTime = 0f;

        var timeData = renderGraph.GetResource<TimeData>();
        var renderDeltaTime = (float)(timeData.time - previousTime);
        previousTimeCache[viewPassData.viewId] = timeData.time;

        // TODO: could make some of these float3's and pack with another float
        var data = new ViewDataStruct
        (
            worldToView,
            worldToClip,
            worldToScreen,
            worldToPreviousScreen,
            worldToPixel,

            viewToWorld,
            viewToClip,
            viewToScreen,
            viewToPixel,

            clipToWorld,
            clipToView,
            clipToScreen,
            clipToPixel,

            screenToWorld,
            screenToView,
            screenToClip,
            screenToPixel,

            pixelToWorld,
            pixelToWorldDir,
            pixelToView,
            pixelToClip,
            pixelToScreen,

            viewPosition,
            viewPosition.y + sky.PlanetRadius * sky.EarthScale,
            new Float4(viewRotation.Rotate(new Float3(viewPassData.tanHalfFov.x * (-1 + -jitter.x), viewPassData.tanHalfFov.y * (-1 + -jitter.y), 1.0f)), 0),
            new Float4(viewRotation.Rotate(new Float3(viewPassData.tanHalfFov.x * (-1 + -jitter.x), viewPassData.tanHalfFov.y * (3 + -jitter.y), 1.0f)), 0),
            new Float4(viewRotation.Rotate(new Float3(viewPassData.tanHalfFov.x * (3 + -jitter.x), viewPassData.tanHalfFov.y * (-1 + -jitter.y), 1.0f)), 0),
            (far - near) * Math.Rcp(near * far),
            Math.Rcp(far),
            near,
            far,
            (float)viewPassData.viewSize.x,
            (float)viewPassData.viewSize.y,
            Math.Rcp(viewPassData.viewSize.x),
            Math.Rcp(viewPassData.viewSize.y),
            viewPassData.viewSize.x - 1,
            viewPassData.viewSize.y - 1,
            viewPassData.tanHalfFov.x / viewPassData.tanHalfFov.y,
            viewPassData.tanHalfFov.y,
            pixelToViewScaleOffset,
            renderDeltaTime,
            previousTransform.Item1,

            Float4.Zero,
            Float4.Zero
        );

        var size = UnsafeUtility.SizeOf<ViewDataStruct>();
        var buffer = renderGraph.GetBuffer(1, size, GraphicsBuffer.Target.Constant);

        using var pass = renderGraph.AddGenericRenderPass("Set View Data Constant Buffer", (data, buffer, size, previousViewScaleLimit, viewPassData.viewId, viewPassData.viewSize, renderGraph.RtHandleSystem));
        pass.WriteBuffer("", buffer);
        pass.SetRenderFunction(static (command, pass, data) =>
        {
            // Calculate current and previous scale/limit
            var currentScaleLimit = GraphicsUtilities.ScaleLimit(data.viewSize, data.RtHandleSystem.ScreenSize);

            if (!data.previousViewScaleLimit.TryGetValue(data.viewId, out var previousViewScaleLimit))
                previousViewScaleLimit = currentScaleLimit;

            data.previousViewScaleLimit[data.viewId] = currentScaleLimit;
            data.data.CurrentScaleLimit = currentScaleLimit;
            data.data.PreviousScaleLimit = previousViewScaleLimit;

            Span<ViewDataStruct> array = stackalloc ViewDataStruct[1];
            array[0] = data.data;
            command.SetBufferData(pass.GetBuffer(data.buffer), array);
        });

        renderGraph.SetResource<ViewData>(new(buffer));

        var cullingPlanes = new CullingPlanes() { Count = 6 };
        for (var i = FrustumPlane.Left; i < FrustumPlane.Count; i++)
            cullingPlanes.SetCullingPlane((int)i, worldToClip.GetFrustumPlane(i));

        renderGraph.SetResource(new CullingPlanesData(cullingPlanes));
    }
}

internal struct ViewDataStruct
{
    public Float4x4 worldToView;
    public Float4x4 worldToClip;
    public Float4x4 worldToScreen;
    public Float4x4 worldToPreviousScreen;
    public Float4x4 worldToPixel;
    public Float4x4 viewToWorld;
    public Float4x4 viewToClip;
    public Float4x4 viewToScreen;
    public Float4x4 viewToPixel;
    public Float4x4 clipToWorld;
    public Float4x4 clipToView;
    public Float4x4 clipToScreen;
    public Float4x4 clipToPixel;
    public Float4x4 screenToWorld;
    public Float4x4 screenToView;
    public Float4x4 screenToClip;
    public Float4x4 screenToPixel;
    public Float4x4 pixelToWorld;
    public Float4x4 pixelToWorldDir;
    public Float4x4 pixelToView;
    public Float4x4 pixelToClip;
    public Float4x4 pixelToScreen;
    public Float3 viewPosition;
    public float Item24;
    public Float4 Item25;
    public Float4 Item26;
    public Float4 Item27;
    public float Item28;
    public float Item29;
    public float near;
    public float far;
    public float Item32;
    public float Item33;
    public float Item34;
    public float Item35;
    public int Item36;
    public int Item37;
    public float aspect;
    public float y;
    public Float4 pixelToViewScaleOffset;
    public float renderDeltaTime;
    public Float3 Item42;
    public Float4 CurrentScaleLimit;
    public Float4 PreviousScaleLimit;

    public ViewDataStruct(Float4x4 worldToView, Float4x4 worldToClip, Float4x4 worldToScreen, Float4x4 worldToPreviousScreen, Float4x4 worldToPixel, Float4x4 viewToWorld, Float4x4 viewToClip, Float4x4 viewToScreen, Float4x4 viewToPixel, Float4x4 clipToWorld, Float4x4 clipToView, Float4x4 clipToScreen, Float4x4 clipToPixel, Float4x4 screenToWorld, Float4x4 screenToView, Float4x4 screenToClip, Float4x4 screenToPixel, Float4x4 pixelToWorld, Float4x4 pixelToWorldDir, Float4x4 pixelToView, Float4x4 pixelToClip, Float4x4 pixelToScreen, Float3 viewPosition, float item24, Float4 item25, Float4 item26, Float4 item27, float item28, float item29, float near, float far, float item32, float item33, float item34, float item35, int item36, int item37, float aspect, float y, Float4 pixelToViewScaleOffset, float renderDeltaTime, Float3 item42, Float4 currentScaleLimit, Float4 previousScaleLimit)
    {
        this.worldToView = worldToView;
        this.worldToClip = worldToClip;
        this.worldToScreen = worldToScreen;
        this.worldToPreviousScreen = worldToPreviousScreen;
        this.worldToPixel = worldToPixel;
        this.viewToWorld = viewToWorld;
        this.viewToClip = viewToClip;
        this.viewToScreen = viewToScreen;
        this.viewToPixel = viewToPixel;
        this.clipToWorld = clipToWorld;
        this.clipToView = clipToView;
        this.clipToScreen = clipToScreen;
        this.clipToPixel = clipToPixel;
        this.screenToWorld = screenToWorld;
        this.screenToView = screenToView;
        this.screenToClip = screenToClip;
        this.screenToPixel = screenToPixel;
        this.pixelToWorld = pixelToWorld;
        this.pixelToWorldDir = pixelToWorldDir;
        this.pixelToView = pixelToView;
        this.pixelToClip = pixelToClip;
        this.pixelToScreen = pixelToScreen;
        this.viewPosition = viewPosition;
        Item24 = item24;
        Item25 = item25;
        Item26 = item26;
        Item27 = item27;
        Item28 = item28;
        Item29 = item29;
        this.near = near;
        this.far = far;
        Item32 = item32;
        Item33 = item33;
        Item34 = item34;
        Item35 = item35;
        Item36 = item36;
        Item37 = item37;
        this.aspect = aspect;
        this.y = y;
        this.pixelToViewScaleOffset = pixelToViewScaleOffset;
        this.renderDeltaTime = renderDeltaTime;
        Item42 = item42;
        CurrentScaleLimit = currentScaleLimit;
        PreviousScaleLimit = previousScaleLimit;
    }
}