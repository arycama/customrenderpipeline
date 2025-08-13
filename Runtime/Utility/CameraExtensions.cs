using UnityEngine;

public static class CameraExtensions
{
	public static float TanHalfFov(this Camera camera) => Geometry.TanHalfFovDegrees(camera.fieldOfView);
}