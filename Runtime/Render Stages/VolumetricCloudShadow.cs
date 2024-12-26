using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class VolumetricCloudShadow : RenderFeature
    {
        private readonly VolumetricClouds.Settings settings;
        private readonly PhysicalSky.Settings physicalSkySettings;
        private readonly Material material;
        private readonly ComputeShader cloudCoverageComputeShader;

        public VolumetricCloudShadow(VolumetricClouds.Settings settings, PhysicalSky.Settings physicalSkySettings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            this.physicalSkySettings = physicalSkySettings;

            cloudCoverageComputeShader = Resources.Load<ComputeShader>("CloudCoverage");
            material = new Material(Shader.Find("Hidden/Volumetric Clouds")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public override void Render()
        {
            var lightDirection = Vector3.up;
            var lightRotation = Quaternion.LookRotation(Vector3.down);

            var atmosphereData = renderGraph.GetResource<AtmospherePropertiesAndTables>();
            var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;

            var planetRadius = physicalSkySettings.PlanetRadius * physicalSkySettings.EarthScale;

            for (var i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                var light = cullingResults.visibleLights[i];
                if (light.lightType != LightType.Directional)
                    continue;

                lightDirection = -light.localToWorldMatrix.Forward();
                lightRotation = light.localToWorldMatrix.rotation;

                // Only 1 light supported
                break;
            }

            var radius = settings.ShadowRadius;
            var resolution = settings.ShadowResolution;
            var res = new Vector4(resolution, resolution, 1f / resolution, 1f / resolution);
            var cameraPosition = renderGraph.GetResource<ViewData>().ViewPosition;
            var texelSize = radius * 2.0f / resolution;
            var snappedCameraPosition = new Vector3(Mathf.Floor(cameraPosition.x / texelSize) * texelSize, Mathf.Floor(cameraPosition.y / texelSize) * texelSize, Mathf.Floor(cameraPosition.z / texelSize) * texelSize);

            var planetCenter = new Vector3(0.0f, -cameraPosition.y - planetRadius, 0.0f);
            var rayOrigin = new Vector3(snappedCameraPosition.x, 0.0f, snappedCameraPosition.z) - cameraPosition;

            // Transform camera bounds to light space
            var boundsMin = rayOrigin + new Vector3(-radius, 0.0f, -radius);
            var boundsSize = new Vector3(radius * 2f, settings.StartHeight + settings.LayerThickness, radius * 2f);
            var worldToLight = Quaternion.Inverse(lightRotation);
            var minValue = Vector3.positiveInfinity;
            var maxValue = Vector3.negativeInfinity;
            for (var z = 0; z < 2; z++)
            {
                for (var y = 0; y < 2; y++)
                {
                    for (var x = 0; x < 2; x++)
                    {
                        var worldPoint = boundsMin + Vector3.Scale(boundsSize, new Vector3(x, y, z));
                        var localPoint = worldToLight * worldPoint;
                        minValue = Vector3.Min(minValue, localPoint);
                        maxValue = Vector3.Max(maxValue, localPoint);

                        // Also raycast each point against the outer planet sphere in the light direction
                        if (GeometryUtilities.IntersectRaySphere(worldPoint - planetCenter, lightDirection, planetRadius + settings.StartHeight + settings.LayerThickness, out var hits) && hits.y > 0.0f)
                        {
                            var worldPoint1 = worldPoint + lightDirection * hits.y;
                            var localPoint1 = worldToLight * worldPoint1;
                            minValue = Vector3.Min(minValue, localPoint1);
                            maxValue = Vector3.Max(maxValue, localPoint1);
                        }
                    }
                }
            }

            var depth = maxValue.z - minValue.z;

            var viewMatrix = Matrix4x4.Rotate(worldToLight);
            var invViewMatrix = Matrix4x4.Rotate(lightRotation);

            var projectionMatrix = Matrix4x4Extensions.OrthoOffCenterNormalized(minValue.x, maxValue.x, minValue.y, maxValue.y, minValue.z, maxValue.z);
            var inverseProjectionMatrix = new Matrix4x4
            {
                m00 = 1.0f / settings.ShadowResolution * (maxValue.x - minValue.x),
                m03 = minValue.x,
                m11 = 1.0f / settings.ShadowResolution * (maxValue.y - minValue.y),
                m13 = minValue.y,
                m23 = minValue.z,
                m33 = 1.0f
            };

            var invViewProjection = invViewMatrix * inverseProjectionMatrix;
            var worldToShadow = projectionMatrix * viewMatrix;

            var cloudShadow = renderGraph.GetTexture(settings.ShadowResolution, settings.ShadowResolution, GraphicsFormat.B10G11R11_UFloatPack32);
            var cloudShadowDataBuffer = renderGraph.SetConstantBuffer((invViewProjection, -lightDirection, 1f / depth, 1f / settings.Density, (float)settings.ShadowSamples, 0.0f, 0.0f));

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Cloud Shadow"))
            {
                pass.Initialize(material, 3);
                pass.WriteTexture(cloudShadow, RenderBufferLoadAction.DontCare);
                pass.ReadBuffer("CloudShadowData", cloudShadowDataBuffer);
                pass.AddRenderPassData<CloudData>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    settings.SetCloudPassData(pass);
                });
            }

            // Cloud coverage
            var cloudCoverageBufferTemp = renderGraph.GetBuffer(1, 16, GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource);
            var cloudCoverageBuffer = renderGraph.GetBuffer(1, 16, GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination);

            var result = new CloudShadowDataResult(cloudShadow, depth, worldToShadow, settings.Density, cloudCoverageBuffer, 0.0f, settings.StartHeight + settings.LayerThickness);
            renderGraph.SetResource(result); ;

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Cloud Coverage"))
            {
                pass.Initialize(cloudCoverageComputeShader, 0, 1);

                pass.AddRenderPassData<CloudData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.WriteBuffer("_Result", cloudCoverageBufferTemp);
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<CloudShadowDataResult>();
                pass.AddRenderPassData<DirectionalLightInfo>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<SkyTransmittanceData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    settings.SetCloudPassData(pass);
                });
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Cloud Coverage Copy"))
            {
                pass.ReadBuffer("", cloudCoverageBufferTemp);
                pass.WriteBuffer("", cloudCoverageBuffer);
                pass.SetRenderFunction((command, pass) =>
                {
                    command.CopyBuffer(cloudCoverageBufferTemp, cloudCoverageBuffer);
                });
            }
        }
    }
}
