using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class RaytracingSystem
    {
        private RenderGraph renderGraph;
        private RayTracingAccelerationStructure rtas;

        public RaytracingSystem(RenderGraph renderGraph)
        {
            this.renderGraph = renderGraph;

            RayTracingAccelerationStructure.RASSettings settings = new RayTracingAccelerationStructure.RASSettings()
            {
                rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                managementMode = RayTracingAccelerationStructure.ManagementMode.Manual,
                layerMask = 255
            };

            rtas = new RayTracingAccelerationStructure(settings);
        }

        ~RaytracingSystem()
        {
            rtas.Dispose();
        }

        public void Build()
        {
            // Calculate any data that does not change across cameras
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("RTAS Update"))
            {
                RayTracingInstanceCullingConfig cullingConfig = new RayTracingInstanceCullingConfig();

                cullingConfig.flags = RayTracingInstanceCullingFlags.None;

                // Disable anyhit shaders for opaque geometries for best ray tracing performance.
                cullingConfig.subMeshFlagsConfig.opaqueMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;

                // Disable transparent geometries.
                cullingConfig.subMeshFlagsConfig.transparentMaterials = RayTracingSubMeshFlags.Disabled;

                // Enable anyhit shaders for alpha-tested / cutout geometries.
                cullingConfig.subMeshFlagsConfig.alphaTestedMaterials = RayTracingSubMeshFlags.Enabled;

                RayTracingInstanceCullingTest instanceTest = new RayTracingInstanceCullingTest();
                instanceTest.allowTransparentMaterials = false;
                instanceTest.allowOpaqueMaterials = true;
                instanceTest.allowAlphaTestedMaterials = true;
                instanceTest.layerMask = -1;
                instanceTest.shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided);
                instanceTest.instanceMask = 1 << 0;

                var instanceTests = new RayTracingInstanceCullingTest[1];
                instanceTests[0] = instanceTest;

                cullingConfig.instanceTests = instanceTests;

                rtas.ClearInstances();
                rtas.CullInstances(ref cullingConfig);

                pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    command.BuildRayTracingAccelerationStructure(rtas);
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new RaytracingResult(rtas));
        }
    }
}