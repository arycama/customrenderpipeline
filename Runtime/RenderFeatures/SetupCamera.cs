using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SetupCamera : ViewRenderFeature
{
	private readonly Sky.Settings sky;
	private readonly Dictionary<int, (Float3, Quaternion, Float4x4)> previousCameraTransform = new();
	private readonly Dictionary<int, double> previousTimeCache = new();

	public SetupCamera(RenderGraph renderGraph, Sky.Settings sky) : base(renderGraph)
	{
		this.sky = sky;
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		var rawJitter = renderGraph.GetResource<TemporalAASetupData>().jitter;

		var jitter = 2.0f * rawJitter / (Float2)viewRenderData.viewSize;

		var near = viewRenderData.near;
		var far = viewRenderData.far;
		var viewPosition = viewRenderData.transform.position;
		var viewRotation = viewRenderData.transform.rotation;

        // Matrices
        // Screen
        var screenToPixel = Float4x4.Scale(new Float3((Float2)viewRenderData.viewSize, 1));
        var pixelToScreen = Float4x4.Scale(new Float3(1 / (Float2)viewRenderData.viewSize, 1));

        // Clip
        var clipToScreen = Float4x4.ScaleOffset(new Float3(0.5f, -0.5f, 1), new Float2(0.5f, 0).xxy);
        var screenToClip = Float4x4.ScaleOffset(new Float3(2, -2, 1), new Float3(-1, 1, 0));
        var clipToPixel = screenToPixel.Mul(clipToScreen);
        var pixelToClip = screenToClip.Mul(pixelToScreen);

        // View
        var viewToNonJitteredClip = Float4x4.PerspectiveReverseZ(viewRenderData.tanHalfFov, near, far);
        var nonJitteredClipToView = Float4x4.PerspectiveReverseZInverse(viewRenderData.tanHalfFov, near, far);

		var viewToClip = viewToNonJitteredClip;
		viewToClip.m02 = jitter.x;
		viewToClip.m12 = jitter.y;
        var clipToView = nonJitteredClipToView;
        clipToView.m03 = viewRenderData.tanHalfFov.x * jitter.x;
        clipToView.m13 = viewRenderData.tanHalfFov.y * jitter.y;

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
		if (!previousCameraTransform.TryGetValue(viewRenderData.viewId, out var previousTransform))
			previousTransform = (viewPosition, viewRotation, viewToNonJitteredClip);

		previousCameraTransform[viewRenderData.viewId] = (viewPosition, viewRotation, viewToNonJitteredClip);

		var worldToPreviousView = Float4x4.WorldToLocal(previousTransform.Item1 - viewPosition, previousTransform.Item2);
		var worldToPreviousClip = previousTransform.Item3.Mul(worldToPreviousView);

		var pixelToWorldDir = Float4x4.PixelToWorldViewDirectionMatrix(viewRenderData.viewSize, jitter, viewRenderData.tanHalfFov, viewToWorld, true, false);

		var clipToPreviousClip = worldToPreviousClip.Mul(clipToWorld);

		// TODO: I think this is similar to pixel to world view dir matrix, maybe make a shared function
		var pixelToViewScaleOffset = new Float4
        (viewRenderData.tanHalfFov.x * 2.0f / viewRenderData.viewSize.x, 
        -viewRenderData.tanHalfFov.y * 2.0f / viewRenderData.viewSize.y, 
        -viewRenderData.tanHalfFov.x * (1.0f - jitter.x), 
        viewRenderData.tanHalfFov.y * (1.0f - jitter.y));

		if(!previousTimeCache.TryGetValue(viewRenderData.viewId, out var previousTime))
			previousTime = 0f;

		var timeData = renderGraph.GetResource<TimeData>();
		var renderDeltaTime = (float)(timeData.time - previousTime);
		previousTimeCache[viewRenderData.viewId] = timeData.time;

        // TODO: could make some of these float3's and pack with another float
        renderGraph.SetResource(new ViewData(renderGraph.SetConstantBuffer(
		(
			worldToView,
			worldToClip,
			worldToPreviousClip,
			worldToScreen,
			worldToPixel,

			viewToWorld,
			viewToClip,
            viewToScreen,
			viewToPixel,

			clipToWorld,
			clipToView,
			clipToScreen,
			clipToPixel,
			clipToPreviousClip,

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
			new Float4(viewRotation.Rotate(new Float3(viewRenderData.tanHalfFov.x * (-1 + jitter.x), viewRenderData.tanHalfFov.y * (-1 + jitter.y), 1.0f)), 0),
			new Float4(viewRotation.Rotate(new Float3(viewRenderData.tanHalfFov.x * (-1 + jitter.x), viewRenderData.tanHalfFov.y * (3 + jitter.y), 1.0f)), 0),
			new Float4(viewRotation.Rotate(new Float3(viewRenderData.tanHalfFov.x * (3 + jitter.x), viewRenderData.tanHalfFov.y * (-1 + jitter.y), 1.0f)), 0),
			(far - near) * Math.Rcp(near * far),
			Math.Rcp(far),
			near,
			far,
            (float)viewRenderData.viewSize.x,
            (float)viewRenderData.viewSize.y,
            Math.Rcp(viewRenderData.viewSize.x),
			Math.Rcp(viewRenderData.viewSize.y),
            viewRenderData.viewSize.x - 1,
            viewRenderData.viewSize.y - 1,
			viewRenderData.camera.aspect,
            viewRenderData.tanHalfFov.y,
			pixelToViewScaleOffset,
			renderDeltaTime,
			previousTransform.Item1
		))));

		var cullingPlanes = new CullingPlanes() { Count = 6 };
		for (var i = 0; i < 6; i++)
			cullingPlanes.SetCullingPlane(i, worldToClip.FrustumPlane(i));

		renderGraph.SetResource(new CullingPlanesData(cullingPlanes));
	}
}
