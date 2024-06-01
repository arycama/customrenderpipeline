using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class RaytracingSystem : IDisposable
    {
        private RenderGraph renderGraph;
        private RayTracingAccelerationStructure rtas;
        private LayerMask layerMask;

        public RaytracingSystem(RenderGraph renderGraph, LayerMask layerMask)
        {
            this.renderGraph = renderGraph;
            this.layerMask = layerMask;

            RayTracingAccelerationStructure.RASSettings settings = new RayTracingAccelerationStructure.RASSettings();
            settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
            settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Manual;
            settings.layerMask = layerMask;
            rtas = new RayTracingAccelerationStructure(settings);
        }

        public void Build(Camera camera)
        {
            RayTracingInstanceCullingConfig cullingConfig = new RayTracingInstanceCullingConfig();

            cullingConfig.flags = RayTracingInstanceCullingFlags.None;

            // Disable anyhit shaders for opaque geometries for best ray tracing performance.
            cullingConfig.subMeshFlagsConfig.opaqueMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;

            // Disable transparent geometries.
            cullingConfig.subMeshFlagsConfig.transparentMaterials = RayTracingSubMeshFlags.Disabled;

            // Enable anyhit shaders for alpha-tested / cutout geometries.
            cullingConfig.subMeshFlagsConfig.alphaTestedMaterials = RayTracingSubMeshFlags.Enabled;

            List<RayTracingInstanceCullingTest> instanceTests = new List<RayTracingInstanceCullingTest>();

            RayTracingInstanceCullingTest instanceTest = new RayTracingInstanceCullingTest();
            instanceTest.allowTransparentMaterials = false;
            instanceTest.allowOpaqueMaterials = true;
            instanceTest.allowAlphaTestedMaterials = true;
            instanceTest.layerMask = layerMask;
            instanceTest.shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided);
            instanceTest.instanceMask = 1 << 0;

            instanceTests.Add(instanceTest);

            cullingConfig.instanceTests = instanceTests.ToArray();

            rtas.ClearInstances();
            rtas.CullInstances(ref cullingConfig);

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("RTAS Update"))
            {
                pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    command.BuildRayTracingAccelerationStructure(rtas, camera.transform.position);
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new RaytracingResult(rtas), renderGraph.FrameIndex);
        }

        ~RaytracingSystem()
        {
            DisposeInternal();
        }

        public void Dispose()
        {
            DisposeInternal();
            GC.SuppressFinalize(this);
        }

        private void DisposeInternal()
        {
            if(rtas != null)
                rtas.Dispose();

            rtas = null;
        }
    }
}