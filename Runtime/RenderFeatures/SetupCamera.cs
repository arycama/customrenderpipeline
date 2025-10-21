using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static Math;

public class SetupCamera : CameraRenderFeature
{
	private readonly Sky.Settings sky;
	private readonly Dictionary<Camera, (Vector3, Quaternion, Matrix4x4)> previousCameraTransform = new();
	private readonly Dictionary<Camera, double> previousTimeCache = new();

	public SetupCamera(RenderGraph renderGraph, Sky.Settings sky) : base(renderGraph)
	{
		this.sky = sky;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var rawJitter = renderGraph.GetResource<TemporalAASetupData>().jitter;

		var jitter = 2.0f * rawJitter / (Float2)camera.ScaledViewSize();

		var near = camera.nearClipPlane;
		var far = camera.farClipPlane;
		var aspect = camera.aspect;

		var tanHalfFovY = Tan(0.5f * Radians(camera.fieldOfView));
		var tanHalfFovX = tanHalfFovY * aspect;

		var viewForward = (Float3)camera.transform.forward;
		var viewPosition = (Float3)camera.cameraToWorldMatrix.GetColumn(3);// camera.transform.position;

		var cameraToWorld = camera.cameraToWorldMatrix;
		cameraToWorld.SetColumn(2, -cameraToWorld.GetColumn(2));

		var viewRotation = cameraToWorld.rotation; // camera.transform.rotation;

		var viewToWorld = Matrix4x4.Rotate(viewRotation);

		// Screen
		var screenToPixel = new Matrix4x4
		{
			m00 = camera.scaledPixelWidth,
			m11 = camera.scaledPixelHeight,
			m22 = 1,
			m33 = 1
		};

		// Clip
		var clipToScreen = new Matrix4x4
		{
			m00 = 0.5f,
			m03 = 0.5f,
			m11 = -0.5f,
			m13 = 0.5f,
			m22 = 1,
			m33 = 1
		};

		var clipToPixel = screenToPixel * clipToScreen;

		// View
		var viewToNonJitteredClip = new Matrix4x4
		{
			m00 = 1.0f / tanHalfFovX,
			m11 = 1.0f / tanHalfFovY,
			m22 = near / (near - far),
			m23 = far * near / (far - near),
			m32 = 1.0f
		};

		var viewToClip = viewToNonJitteredClip;
		viewToClip.m02 = -jitter.x;
		viewToClip.m11 = -viewToClip.m11;
		viewToClip.m12 = jitter.y;

		var viewToScreen = clipToScreen * viewToClip;
		var viewToPixel = clipToPixel * viewToClip;

		// World
		var worldToView = Matrix4x4.Transpose(viewToWorld);
		var worldToClip = viewToClip * worldToView; 
		var worldToScreen = viewToScreen * worldToView;
		var worldToPixel = viewToPixel * worldToView;

		// Inverse matrices
		var nonJitteredClipToView = new Matrix4x4
		{
			m00 = tanHalfFovX,
			m11 = tanHalfFovY,
			m23 = 1.0f,
			m32 = (far - near) / (near * far),
			m33 = 1.0f / far
		};

		var jitteredClipToView = nonJitteredClipToView;
		jitteredClipToView.m03 = tanHalfFovX * jitter.x;
		jitteredClipToView.m13 = tanHalfFovY * jitter.y;

		var screenToView = jitteredClipToView;
		screenToView.m00 *= 2.0f;
		screenToView.m11 *= 2.0f;
		screenToView.m03 -= tanHalfFovX;
		screenToView.m13 -= tanHalfFovY;

		var pixelToView = screenToView;
		pixelToView.m00 /= camera.scaledPixelWidth;
		pixelToView.m11 /= camera.scaledPixelHeight;

		var jitteredClipToWorld = viewToWorld * jitteredClipToView;
		var pixelToWorld = viewToWorld * pixelToView;
		var screenToWorld = viewToWorld * screenToView;

		// Previous frame matrices
		if (!previousCameraTransform.TryGetValue(camera, out var previousTransform))
			previousTransform = (viewPosition, viewRotation, viewToNonJitteredClip);

		previousCameraTransform[camera] = (viewPosition, viewRotation, viewToNonJitteredClip);

		//var worldToPreviousView = Matrix4x4Extensions.WorldToLocal(previousTransform.Item1 - viewPosition, previousTransform.Item2);
		var worldToPreviousView = Matrix4x4Extensions.WorldToLocal(previousTransform.Item1 - viewPosition, previousTransform.Item2);
		var worldToPreviousClip = previousTransform.Item3 * worldToPreviousView;

		var pixelToWorldDir = Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(camera.scaledPixelWidth, camera.scaledPixelHeight, jitter, tanHalfFovY, camera.aspect, viewToWorld, false, false);

		var clipToPreviousClip = worldToPreviousClip * jitteredClipToWorld;

		var pixelToViewScaleOffset = new Float4(tanHalfFovX * 2.0f / camera.scaledPixelWidth, tanHalfFovY * 2.0f / camera.scaledPixelHeight, -tanHalfFovX * (1.0f - jitter.x), -tanHalfFovY * (1.0f + jitter.y));

		if(!previousTimeCache.TryGetValue(camera, out var previousTime))
			previousTime = 0f;

		var timeData = renderGraph.GetResource<TimeData>();
		var renderDeltaTime = (float)(timeData.time - previousTime);
		previousTimeCache[camera] = timeData.time;

	// TODO: could make some of these float3's and pack with another float
	renderGraph.SetResource(new ViewData(renderGraph.SetConstantBuffer((
			worldToView,
			worldToClip,
			worldToPreviousClip,
			worldToScreen,
			worldToPixel,
			viewToWorld,
			viewToClip,
			viewToPixel,
			jitteredClipToWorld,
			jitteredClipToView,
			clipToScreen,
			clipToPixel,
			clipToPreviousClip,
			pixelToWorld,
			pixelToWorldDir,
			pixelToView,
			viewPosition,
			viewPosition.y + sky.PlanetRadius * sky.EarthScale,
			new Float4(viewRotation.Rotate(new Float3(tanHalfFovX * (-1.0f + jitter.x), tanHalfFovY * (1.0f + jitter.y), 1.0f)), 0),
			new Float4(viewRotation.Rotate(new Float3(tanHalfFovX * (3.0f + jitter.x), tanHalfFovY * (1.0f + jitter.y), 1.0f)), 0),
			new Float4(viewRotation.Rotate(new Float3(tanHalfFovX * (-1.0f + jitter.x), tanHalfFovY * (-3.0f + jitter.y), 1.0f)), 0),
			(far - near) * Math.Rcp(near * far),
			Math.Rcp(far),
			near,
			far,
			(float)camera.scaledPixelWidth,
			(float)camera.scaledPixelHeight,
			Math.Rcp(camera.scaledPixelWidth),
			Math.Rcp(camera.scaledPixelHeight),
			camera.scaledPixelWidth - 1,
			camera.scaledPixelHeight - 1,
			camera.aspect,
			camera.TanHalfFov(),
			pixelToViewScaleOffset,
			renderDeltaTime,
			0f,
			0f,
			0f
		))));

		using (var pass = renderGraph.AddGenericRenderPass("Set View Properties", (viewPosition, viewRotation, tanHalfFovX, tanHalfFovY, jitter)))
		{
			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetVector("ViewPosition1", data.viewPosition);
				pass.SetVectorArray("FrustumCorners1", new Vector4[3]
				{
					new Float4(data.viewRotation.Rotate(new Float3(data.tanHalfFovX * (-1.0f + data.jitter.x), data.tanHalfFovY * (1.0f + data.jitter.y), 1.0f)), 0),
					new Float4(data.viewRotation.Rotate(new Float3(data.tanHalfFovX * (3.0f + data.jitter.x), data.tanHalfFovY * (1.0f + data.jitter.y), 1.0f)), 0),
					new Float4(data.viewRotation.Rotate(new Float3(data.tanHalfFovX * (-1.0f + data.jitter.x), data.tanHalfFovY * (-3.0f + data.jitter.y), 1.0f)), 0),
				});
			});
		}

		var frustumPlanes = ArrayPool<Plane>.Get(6);
		var cameraProjMatrix = camera.projectionMatrix;
		cameraProjMatrix.m02 = jitter.x;
		cameraProjMatrix.m12 = jitter.y;
		cameraProjMatrix.SetColumn(2, -cameraProjMatrix.GetColumn(2));
		GeometryUtility.CalculateFrustumPlanes(cameraProjMatrix * worldToView, frustumPlanes);

		var cullingPlanes = new CullingPlanes() { Count = 6 };
		for (var i = 0; i < 6; i++)
			cullingPlanes.SetCullingPlane(i, frustumPlanes[i]);

		renderGraph.SetResource(new CullingPlanesData(cullingPlanes));

		ArrayPool<Plane>.Release(frustumPlanes);
	}
}
