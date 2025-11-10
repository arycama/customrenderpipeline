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

    public static Float4x4 GetGpuViewProjectionMatrix(this Camera camera, Camera.StereoscopicEye eye = Camera.StereoscopicEye.Left, bool renderIntoTexture = true)
    {
        var worldToView = camera.stereoEnabled ? camera.GetStereoViewMatrix(eye) : camera.worldToCameraMatrix;
        var viewToClip = camera.stereoEnabled ? camera.GetStereoProjectionMatrix(eye) : camera.projectionMatrix;
        return GL.GetGPUProjectionMatrix(viewToClip, renderIntoTexture) * worldToView;
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
        var offset = index switch
        {
            0 => new Float2(-1, 1),
            1 => new Float2(3, 1),
            2 => new Float2(-1, -3),
            _ => throw new ArgumentOutOfRangeException(index.ToString()),
        };

        var tanHalfFov = camera.TanHalfFov();
        var viewRotation = camera.GetViewRotation(eye);
        return viewRotation.Rotate(new Float3((offset + jitter) * tanHalfFov, 1));
    }
}