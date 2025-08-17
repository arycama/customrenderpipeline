using UnityEngine;

public static class CameraExtensions
{
	public static float TanHalfFov(this Camera camera) => Geometry.TanHalfFovDegrees(camera.fieldOfView);

	public static Int2 ViewSize(this Camera camera) => new(camera.pixelWidth, camera.pixelHeight);

	public static Int2 ScaledViewSize(this Camera camera) => new(camera.scaledPixelWidth, camera.scaledPixelHeight);
}