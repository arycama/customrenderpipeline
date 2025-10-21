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
		renderGraph.SetResource(new ViewData(renderGraph.SetConstantBuffer(new ViewDataTemp
		(
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
			camera.scaledPixelWidth,
			camera.scaledPixelHeight,
			Math.Rcp(camera.scaledPixelWidth),
			Math.Rcp(camera.scaledPixelHeight),
			camera.scaledPixelWidth - 1,
			camera.scaledPixelHeight - 1,
			camera.aspect,
			camera.TanHalfFov(),
			pixelToViewScaleOffset,
			renderDeltaTime,
			previousTransform.Item1
		))));

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

internal struct ViewDataTemp
{
	public Matrix4x4 worldToView;
	public Matrix4x4 worldToClip;
	public Matrix4x4 worldToPreviousClip;
	public Matrix4x4 worldToScreen;
	public Matrix4x4 worldToPixel;
	public Matrix4x4 viewToWorld;
	public Matrix4x4 viewToClip;
	public Matrix4x4 viewToPixel;
	public Matrix4x4 jitteredClipToWorld;
	public Matrix4x4 jitteredClipToView;
	public Matrix4x4 clipToScreen;
	public Matrix4x4 clipToPixel;
	public Matrix4x4 clipToPreviousClip;
	public Matrix4x4 pixelToWorld;
	public Matrix4x4 pixelToWorldDir;
	public Matrix4x4 pixelToView;
	public Float3 viewPosition;
	public float Item18;
	public Float4 Item19;
	public Float4 Item20;
	public Float4 Item21;
	public float Item22;
	public float Item23;
	public float near;
	public float far;
	public float Item26;
	public float Item27;
	public float Item28;
	public float Item29;
	public int Item30;
	public int Item31;
	public float aspect;
	public float Item33;
	public Float4 pixelToViewScaleOffset;
	public float renderDeltaTime;
	public Vector3 Item36;

	public ViewDataTemp(Matrix4x4 worldToView, Matrix4x4 worldToClip, Matrix4x4 worldToPreviousClip, Matrix4x4 worldToScreen, Matrix4x4 worldToPixel, Matrix4x4 viewToWorld, Matrix4x4 viewToClip, Matrix4x4 viewToPixel, Matrix4x4 jitteredClipToWorld, Matrix4x4 jitteredClipToView, Matrix4x4 clipToScreen, Matrix4x4 clipToPixel, Matrix4x4 clipToPreviousClip, Matrix4x4 pixelToWorld, Matrix4x4 pixelToWorldDir, Matrix4x4 pixelToView, Float3 viewPosition, float item18, Float4 item19, Float4 item20, Float4 item21, float item22, float item23, float near, float far, float item26, float item27, float item28, float item29, int item30, int item31, float aspect, float item33, Float4 pixelToViewScaleOffset, float renderDeltaTime, Vector3 item36)
	{
		this.worldToView = worldToView;
		this.worldToClip = worldToClip;
		this.worldToPreviousClip = worldToPreviousClip;
		this.worldToScreen = worldToScreen;
		this.worldToPixel = worldToPixel;
		this.viewToWorld = viewToWorld;
		this.viewToClip = viewToClip;
		this.viewToPixel = viewToPixel;
		this.jitteredClipToWorld = jitteredClipToWorld;
		this.jitteredClipToView = jitteredClipToView;
		this.clipToScreen = clipToScreen;
		this.clipToPixel = clipToPixel;
		this.clipToPreviousClip = clipToPreviousClip;
		this.pixelToWorld = pixelToWorld;
		this.pixelToWorldDir = pixelToWorldDir;
		this.pixelToView = pixelToView;
		this.viewPosition = viewPosition;
		Item18 = item18;
		Item19 = item19;
		Item20 = item20;
		Item21 = item21;
		Item22 = item22;
		Item23 = item23;
		this.near = near;
		this.far = far;
		Item26 = item26;
		Item27 = item27;
		Item28 = item28;
		Item29 = item29;
		Item30 = item30;
		Item31 = item31;
		this.aspect = aspect;
		Item33 = item33;
		this.pixelToViewScaleOffset = pixelToViewScaleOffset;
		this.renderDeltaTime = renderDeltaTime;
		Item36 = item36;
	}

	public override bool Equals(object obj) => obj is ViewDataTemp other && worldToView.Equals(other.worldToView) && worldToClip.Equals(other.worldToClip) && worldToPreviousClip.Equals(other.worldToPreviousClip) && worldToScreen.Equals(other.worldToScreen) && worldToPixel.Equals(other.worldToPixel) && viewToWorld.Equals(other.viewToWorld) && viewToClip.Equals(other.viewToClip) && viewToPixel.Equals(other.viewToPixel) && jitteredClipToWorld.Equals(other.jitteredClipToWorld) && jitteredClipToView.Equals(other.jitteredClipToView) && clipToScreen.Equals(other.clipToScreen) && clipToPixel.Equals(other.clipToPixel) && clipToPreviousClip.Equals(other.clipToPreviousClip) && pixelToWorld.Equals(other.pixelToWorld) && pixelToWorldDir.Equals(other.pixelToWorldDir) && pixelToView.Equals(other.pixelToView) && viewPosition.Equals(other.viewPosition) && Item18 == other.Item18 && EqualityComparer<Float4>.Default.Equals(Item19, other.Item19) && EqualityComparer<Float4>.Default.Equals(Item20, other.Item20) && EqualityComparer<Float4>.Default.Equals(Item21, other.Item21) && Item22 == other.Item22 && Item23 == other.Item23 && near == other.near && far == other.far && Item26 == other.Item26 && Item27 == other.Item27 && Item28 == other.Item28 && Item29 == other.Item29 && Item30 == other.Item30 && Item31 == other.Item31 && aspect == other.aspect && Item33 == other.Item33 && EqualityComparer<Float4>.Default.Equals(pixelToViewScaleOffset, other.pixelToViewScaleOffset) && renderDeltaTime == other.renderDeltaTime && Item36.Equals(other.Item36);

	public override int GetHashCode()
	{
		var hash = new System.HashCode();
		hash.Add(worldToView);
		hash.Add(worldToClip);
		hash.Add(worldToPreviousClip);
		hash.Add(worldToScreen);
		hash.Add(worldToPixel);
		hash.Add(viewToWorld);
		hash.Add(viewToClip);
		hash.Add(viewToPixel);
		hash.Add(jitteredClipToWorld);
		hash.Add(jitteredClipToView);
		hash.Add(clipToScreen);
		hash.Add(clipToPixel);
		hash.Add(clipToPreviousClip);
		hash.Add(pixelToWorld);
		hash.Add(pixelToWorldDir);
		hash.Add(pixelToView);
		hash.Add(viewPosition);
		hash.Add(Item18);
		hash.Add(Item19);
		hash.Add(Item20);
		hash.Add(Item21);
		hash.Add(Item22);
		hash.Add(Item23);
		hash.Add(near);
		hash.Add(far);
		hash.Add(Item26);
		hash.Add(Item27);
		hash.Add(Item28);
		hash.Add(Item29);
		hash.Add(Item30);
		hash.Add(Item31);
		hash.Add(aspect);
		hash.Add(Item33);
		hash.Add(pixelToViewScaleOffset);
		hash.Add(renderDeltaTime);
		hash.Add(Item36);
		return hash.ToHashCode();
	}

	public void Deconstruct(out Matrix4x4 worldToView, out Matrix4x4 worldToClip, out Matrix4x4 worldToPreviousClip, out Matrix4x4 worldToScreen, out Matrix4x4 worldToPixel, out Matrix4x4 viewToWorld, out Matrix4x4 viewToClip, out Matrix4x4 viewToPixel, out Matrix4x4 jitteredClipToWorld, out Matrix4x4 jitteredClipToView, out Matrix4x4 clipToScreen, out Matrix4x4 clipToPixel, out Matrix4x4 clipToPreviousClip, out Matrix4x4 pixelToWorld, out Matrix4x4 pixelToWorldDir, out Matrix4x4 pixelToView, out Float3 viewPosition, out float item18, out Float4 item19, out Float4 item20, out Float4 item21, out float item22, out float item23, out float near, out float far, out float item26, out float item27, out float item28, out float item29, out int item30, out int item31, out float aspect, out float item33, out Float4 pixelToViewScaleOffset, out float renderDeltaTime, out Vector3 item36)
	{
		worldToView = this.worldToView;
		worldToClip = this.worldToClip;
		worldToPreviousClip = this.worldToPreviousClip;
		worldToScreen = this.worldToScreen;
		worldToPixel = this.worldToPixel;
		viewToWorld = this.viewToWorld;
		viewToClip = this.viewToClip;
		viewToPixel = this.viewToPixel;
		jitteredClipToWorld = this.jitteredClipToWorld;
		jitteredClipToView = this.jitteredClipToView;
		clipToScreen = this.clipToScreen;
		clipToPixel = this.clipToPixel;
		clipToPreviousClip = this.clipToPreviousClip;
		pixelToWorld = this.pixelToWorld;
		pixelToWorldDir = this.pixelToWorldDir;
		pixelToView = this.pixelToView;
		viewPosition = this.viewPosition;
		item18 = Item18;
		item19 = Item19;
		item20 = Item20;
		item21 = Item21;
		item22 = Item22;
		item23 = Item23;
		near = this.near;
		far = this.far;
		item26 = Item26;
		item27 = Item27;
		item28 = Item28;
		item29 = Item29;
		item30 = Item30;
		item31 = Item31;
		aspect = this.aspect;
		item33 = Item33;
		pixelToViewScaleOffset = this.pixelToViewScaleOffset;
		renderDeltaTime = this.renderDeltaTime;
		item36 = Item36;
	}

	public static implicit operator (Matrix4x4 worldToView, Matrix4x4 worldToClip, Matrix4x4 worldToPreviousClip, Matrix4x4 worldToScreen, Matrix4x4 worldToPixel, Matrix4x4 viewToWorld, Matrix4x4 viewToClip, Matrix4x4 viewToPixel, Matrix4x4 jitteredClipToWorld, Matrix4x4 jitteredClipToView, Matrix4x4 clipToScreen, Matrix4x4 clipToPixel, Matrix4x4 clipToPreviousClip, Matrix4x4 pixelToWorld, Matrix4x4 pixelToWorldDir, Matrix4x4 pixelToView, Float3 viewPosition, float, Float4, Float4, Float4, float, float, float near, float far, float, float, float, float, int, int, float aspect, float, Float4 pixelToViewScaleOffset, float renderDeltaTime, Vector3)(ViewDataTemp value) => (value.worldToView, value.worldToClip, value.worldToPreviousClip, value.worldToScreen, value.worldToPixel, value.viewToWorld, value.viewToClip, value.viewToPixel, value.jitteredClipToWorld, value.jitteredClipToView, value.clipToScreen, value.clipToPixel, value.clipToPreviousClip, value.pixelToWorld, value.pixelToWorldDir, value.pixelToView, value.viewPosition, value.Item18, value.Item19, value.Item20, value.Item21, value.Item22, value.Item23, value.near, value.far, value.Item26, value.Item27, value.Item28, value.Item29, value.Item30, value.Item31, value.aspect, value.Item33, value.pixelToViewScaleOffset, value.renderDeltaTime, value.Item36);
	public static implicit operator ViewDataTemp((Matrix4x4 worldToView, Matrix4x4 worldToClip, Matrix4x4 worldToPreviousClip, Matrix4x4 worldToScreen, Matrix4x4 worldToPixel, Matrix4x4 viewToWorld, Matrix4x4 viewToClip, Matrix4x4 viewToPixel, Matrix4x4 jitteredClipToWorld, Matrix4x4 jitteredClipToView, Matrix4x4 clipToScreen, Matrix4x4 clipToPixel, Matrix4x4 clipToPreviousClip, Matrix4x4 pixelToWorld, Matrix4x4 pixelToWorldDir, Matrix4x4 pixelToView, Float3 viewPosition, float, Float4, Float4, Float4, float, float, float near, float far, float, float, float, float, int, int, float aspect, float, Float4 pixelToViewScaleOffset, float renderDeltaTime, Vector3) value) => new ViewDataTemp(value.worldToView, value.worldToClip, value.worldToPreviousClip, value.worldToScreen, value.worldToPixel, value.viewToWorld, value.viewToClip, value.viewToPixel, value.jitteredClipToWorld, value.jitteredClipToView, value.clipToScreen, value.clipToPixel, value.clipToPreviousClip, value.pixelToWorld, value.pixelToWorldDir, value.pixelToView, value.viewPosition, value.Item18, value.Item19, value.Item20, value.Item21, value.Item22, value.Item23, value.near, value.far, value.Item26, value.Item27, value.Item28, value.Item29, value.Item30, value.Item31, value.aspect, value.Item33, value.pixelToViewScaleOffset, value.renderDeltaTime, value.Item36);
}