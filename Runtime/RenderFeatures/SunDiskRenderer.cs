using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unmath;
using static Unmath.Math;

namespace CustomRenderPipeline
{
    public class SunDiskRenderer : ViewRenderFeature
    {
        private readonly LightingSettings settings;
        private readonly Material celestialBodyMaterial;

        public SunDiskRenderer(RenderGraph renderGraph, LightingSettings settings) : base(renderGraph)
        {
            this.settings = settings;
            celestialBodyMaterial = new Material(Shader.Find("Surface/Celestial Body")) { hideFlags = HideFlags.HideAndDontSave };
        }

        // Precomputed Airy ring peaks, y = (2*J1(x)/x)^2
        // TODO: Can we compute this manually on init instead of pre-computed?
        public static readonly (float x, float relIntensity)[] Rings = new (float, float)[]
        {
        (5.13562f,  0.01749841f),
        (8.41724f,  0.00415841f),
        (11.61984f, 0.00160059f),
        (14.79595f, 0.00077944f),
        (17.95982f, 0.00043710f),
        (21.11698f, 0.00026792f),
        (24.27011f, 0.00017451f),
        (27.42057f, 0.00011880f),
        (30.56920f, 0.00008367f),
        (33.71652f, 0.00006057f),
        (36.86285f, 0.00004486f),
        (40.00845f, 0.00003385f),
        (43.15345f, 0.00002596f),
        (46.29798f, 0.00002019f),
        (49.44211f, 0.00001590f),
        (52.58590f, 0.00001266f),
        };

        // Finds the airy disc radius peak before the luminance drops below the visibility threshold
        public static float GetLastVisibleX(float peakBrightness, float threshold)
        {
            //return 3.83171f;
            //return Rings[0].x;

            if (peakBrightness <= threshold)
                return 0f;

            // Start from ring 1 and go outward
            for (int i = 1; i < Rings.Length; i++)
            {
                float intensity = peakBrightness * Rings[i].relIntensity;
                if (intensity < threshold)
                    return Rings[i - 1].x; // Previous ring's peak
            }

            // All rings in table are visible
            return Rings[^1].x;
        }

        public static float GetVisibleAngularDiameter(float peakBrightness, float threshold, float apertureDiameter, float focalLength, float sensorSize, float wavelength, out float maxX)
        {
            var x = GetLastVisibleX(peakBrightness, threshold);

            // Convert from x = k * a * sinTheta to sinTheta? k is 2 pi / wavelength, a is aperture radius.
            var k = 2 * Pi / wavelength;
            var a = 0.5f * apertureDiameter;
            var sinTheta = x / (k * a);
            var cosTheta = Sqrt(1.0f - Sq(sinTheta));
            var tanTheta = sinTheta / cosTheta;

            maxX = x;

            //float theta = (2f * wavelength / (Mathf.PI * apertureDiameter)) * maxX;
            return 2 * tanTheta;// * focalLength / sensorSize;

            //var sinTheta = 1.22f * wavelength / apertureDiameter;
        }

        public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
        {
            var lightData = renderGraph.GetResource<LightingData>();
            var viewPosition = viewPassData.position;

            // 1. Considering the sun as a perfect disk, evaluate its solid angle
            var solidAngle = Geometry.ConeAngleToSolidAngleDegrees(0.53f);

            // 2. Evaluate sun luiminance at ground level accoridng to solidAngle and illuminance at zenith (noon)
            var luminance = lightData.light0Color / solidAngle;

            var camera = viewPassData.camera;
            var sensorSize = MillimeterToMeter(camera.sensorSize.y);
            var focalLength = PhysicalCameraUtility.FocalLength(sensorSize, Geometry.TanHalfFovDegrees(camera.fieldOfView));
            var apertureDiameter = PhysicalCameraUtility.ApertureRadius(focalLength, (float)camera.aperture) * 2;

            var exposure = PhysicalCameraUtility.EV100ToExposure(PhysicalCameraUtility.ComputeEV100(camera.aperture, camera.shutterSpeed, camera.iso));

            // Get the max airy disc radius and store it as the min size of the tracer
            var color = luminance * exposure;
            var r = GetVisibleAngularDiameter(color.x, 0.01f, apertureDiameter, focalLength, sensorSize, NanometerToMeter(630), out var redX);
            var g = GetVisibleAngularDiameter(color.y, 0.01f, apertureDiameter, focalLength, sensorSize, NanometerToMeter(523), out var greenX);
            var b = GetVisibleAngularDiameter(color.z, 0.01f, apertureDiameter, focalLength, sensorSize, NanometerToMeter(467), out var blueX);
            var airyDiscDiameter = Max(r, Max(g, b));

            var maxX = Max(redX, Max(greenX, blueX));
            // var scale = airyDiscDiameter;

            var diameter1 = Atan(airyDiscDiameter);

            var theta = 0.5f * (Radians(settings.SunAngularDiameter));
            var tanTheta = Tan(theta);
            var scale = 2 * tanTheta;
            var matrix = Float4x4.TRS(viewPosition - lightData.light0Rotation.Forward, lightData.light0Rotation.ReverseForward, scale);

            // Recompute intensity to account for the new diameter (Which inc=ludes the approximate airy disc) so that the luminance is normalized
            //luminance.x = PhysicalLightUtility.CandelasToNitsDisc(luminance.x, theta);
            //luminance.y = PhysicalLightUtility.CandelasToNitsDisc(luminance.y, theta);
            //luminance.z = PhysicalLightUtility.CandelasToNitsDisc(luminance.z, theta);

            using (var pass = renderGraph.AddDrawProceduralRenderPass("Sun Disk", (luminance, -lightData.light0Rotation.Forward, maxX, apertureDiameter * 0.5f)))
            {
                pass.Initialize(celestialBodyMaterial, matrix, viewPassData.viewSize, 1, 0, 4, 1, MeshTopology.Quads, isScreenPass: true);

                pass.WriteRtHandleDepth<CameraDepth>(UnityEngine.Rendering.SubPassFlags.ReadOnlyDepthStencil);
                pass.WriteRtHandle<CameraTarget>();

                pass.ReadResource<AutoExposureData>();
                pass.ReadResource<ViewData>();

                pass.ReadResource<AtmospherePropertiesAndTables>();
                pass.ReadResource<TemporalAAData>();

                pass.ReadResource<SkyViewTransmittanceData>();
                pass.ReadResource<CloudShadowDataResult>();

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    pass.SetVector("Luminance", data.luminance);
                    pass.SetVector("Direction", data.Item3);
                    pass.SetFloat("MaxX", data.maxX);
                    pass.SetFloat("ApertureRadius", data.Item4);
                });
            }
        }
    }
}