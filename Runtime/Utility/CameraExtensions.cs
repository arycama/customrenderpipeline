using System;
using UnityEngine;
using static Float3;

public static class CameraExtensions
{
    public static float TanHalfFovY(this Camera camera) => Geometry.TanHalfFovDegrees(camera.fieldOfView);

    public static float TanHalfFovX(this Camera camera) => camera.aspect * camera.TanHalfFovY();

    public static Float2 TanHalfFov(this Camera camera) => new(camera.TanHalfFovX(), camera.TanHalfFovY());

    public static Int2 ViewSize(this Camera camera) => new(camera.pixelWidth, camera.pixelHeight);

	public static Int2 ScaledViewSize(this Camera camera) => new(camera.scaledPixelWidth, camera.scaledPixelHeight);

    public static Float4x4 GetGpuViewProjectionMatrix(this Camera camera, bool renderIntoTexture = true)
    {
        return GL.GetGPUProjectionMatrix(camera.projectionMatrix, renderIntoTexture) * camera.worldToCameraMatrix;
    }

    public static Float4x4 GetStereoGpuViewProjectionMatrix(this Camera camera, Camera.StereoscopicEye eye = Camera.StereoscopicEye.Left, bool renderIntoTexture = true)
    {
        return camera.stereoEnabled ? GL.GetGPUProjectionMatrix(camera.GetStereoProjectionMatrix(eye), renderIntoTexture) * camera.GetStereoViewMatrix(eye) : camera.GetGpuViewProjectionMatrix(renderIntoTexture);
    }

    public static Float3 GetViewPosition(this Camera camera, Camera.StereoscopicEye eye = Camera.StereoscopicEye.Left)
    {
        var worldToView = (Float4x4)(camera.stereoEnabled ? camera.GetStereoViewMatrix(eye) : camera.worldToCameraMatrix);
        var translation = worldToView.c3.xyz;
        return new(-Dot(worldToView.c0.xyz, translation), -Dot(worldToView.c1.xyz, translation), -Dot(worldToView.c2.xyz, translation));
    }

    public static Quaternion GetViewRotation(this Camera camera, Camera.StereoscopicEye eye = Camera.StereoscopicEye.Left)
    {
        var worldToView = (Float4x4)(camera.stereoEnabled ? camera.GetStereoViewMatrix(eye) : camera.worldToCameraMatrix);
        return new Quaternion(worldToView.r0.xyz, worldToView.r1.xyz, -worldToView.r2.xyz);
    }

    public static Float3 GetFrustumCorner(this Camera camera, int index, Camera.StereoscopicEye eye = Camera.StereoscopicEye.Left, Float2 jitter = default)
    {
        // Fullscreen triangle coordinates in clip space
        var clipPosition = index switch
        {
            0 => new Float4(-1, 1, 1, 1),
            1 => new Float4(3, 1, 1, 1),
            2 => new Float4(-1, -3, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        // Transform from clip to view space
        var viewToClip = camera.stereoEnabled ? camera.GetStereoProjectionMatrix(eye) : camera.projectionMatrix;
        var clipToView = viewToClip.inverse;
        var viewPos = clipToView * clipPosition;

        // Transform from view to camera-relative world space (Since we only want the vector from the view to the corner
        var worldToView = camera.stereoEnabled ? camera.GetStereoViewMatrix(eye) : camera.worldToCameraMatrix;
        var viewToWorld = worldToView.inverse;
        viewToWorld.SetColumn(3, new Vector4(0, 0, 0, 1));

        // Reverse the perspective projection
        var cameraRelativeWorldPos = (Float4)(viewToWorld * viewPos);
        return cameraRelativeWorldPos.xyz / cameraRelativeWorldPos.w;
    }
}