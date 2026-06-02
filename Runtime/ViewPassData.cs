using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Unmath;
using Quaternion = Unmath.Quaternion;

public readonly struct ViewPassData
{
    public readonly int parameterStart;
    public readonly int displayInfoIndex;
    public readonly int viewCount;
    public readonly bool isFlipped;
    public readonly Int2 viewSize;
    public readonly SinglePassStereoMode stereoMode;
    public readonly RenderTargetIdentifier target;
    public readonly GraphicsFormat format;
    public readonly VRTextureUsage vrTextureUsage;
    public readonly int antiAliasing;
    public readonly int viewId;
    public readonly ScriptableCullingParameters cullingParameters;
    public readonly int mirrorBlitMode;
    public readonly IntPtr foveatedRenderingInfo;
    public readonly CameraType cameraType;

    public readonly float near;
    public readonly float far;

    public readonly Float3 position;
    public readonly Quaternion rotation;

    public readonly DistanceMetric distanceMetric;
    public readonly Float3 sortAxis;

    public readonly Float2 tanHalfFov;
    public readonly Float2 offset;

    public readonly Camera camera;

    // TODO: Consolidate or seperate
    public readonly float iso;
    public readonly float aperture;
    public readonly float shutterSpeed;
    public readonly float focalLength;
    public readonly float focalDistance;
    public readonly float apertureRadius;
    public readonly float exposure;

    public ViewPassData(int parameterStart, int displayInfoIndex, int viewCount, bool isFlipped, Int2 viewSize, SinglePassStereoMode stereoMode, RenderTargetIdentifier target, GraphicsFormat format, VRTextureUsage vrTextureUsage, int antiAliasing, int viewId, ScriptableCullingParameters cullingParameters, int mirrorBlitMode, IntPtr foveatedRenderingInfo, CameraType cameraType, float near, float far, Float3 position, Quaternion rotation, DistanceMetric distanceMetric, Float3 sortAxis, Float2 tanHalfFov, Float2 offset, Camera camera, float iso, float aperture, float shutterSpeed, float focalLength, float focalDistance, float apertureRadius, float exposure)
    {
        this.parameterStart = parameterStart;
        this.displayInfoIndex = displayInfoIndex;
        this.viewCount = viewCount;
        this.isFlipped = isFlipped;
        this.viewSize = viewSize;
        this.stereoMode = stereoMode;
        this.target = target;
        this.format = format;
        this.vrTextureUsage = vrTextureUsage;
        this.antiAliasing = antiAliasing;
        this.viewId = viewId;
        this.cullingParameters = cullingParameters;
        this.mirrorBlitMode = mirrorBlitMode;
        this.foveatedRenderingInfo = foveatedRenderingInfo;
        this.cameraType = cameraType;
        this.near = near;
        this.far = far;
        this.position = position;
        this.rotation = rotation;
        this.distanceMetric = distanceMetric;
        this.sortAxis = sortAxis;
        this.tanHalfFov = tanHalfFov;
        this.offset = offset;
        this.camera = camera;
        this.iso = iso;
        this.aperture = aperture;
        this.shutterSpeed = shutterSpeed;
        this.focalLength = focalLength;
        this.focalDistance = focalDistance;
        this.apertureRadius = apertureRadius;
        this.exposure = exposure;
    }
}
