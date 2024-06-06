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
        private RayTracingInstanceCullingConfig config;

        public RaytracingSystem(RenderGraph renderGraph, LayerMask layerMask)
        {
            this.renderGraph = renderGraph;
            this.layerMask = layerMask;

            RayTracingAccelerationStructure.RASSettings settings = new RayTracingAccelerationStructure.RASSettings
            {
                rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                managementMode = RayTracingAccelerationStructure.ManagementMode.Manual,
                layerMask = layerMask
            };
            rtas = new RayTracingAccelerationStructure(settings);

            config = new RayTracingInstanceCullingConfig
            {
                flags = RayTracingInstanceCullingFlags.None,
                subMeshFlagsConfig = new RayTracingSubMeshFlagsConfig()
                {
                    opaqueMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly
                    //opaqueMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly,
                    //alphaTestedMaterials = RayTracingSubMeshFlags.Enabled,
                    //transparentMaterials = RayTracingSubMeshFlags.Disabled
                },

                instanceTests = new RayTracingInstanceCullingTest[]
                {
                    new()
                    {
                        //allowTransparentMaterials = false,
                        allowOpaqueMaterials = true,
                        //allowAlphaTestedMaterials = true,
                        layerMask = layerMask,
                        shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided),
                        instanceMask = 1
                    }
                },

                //alphaTestedMaterialConfig = default,
                //lodParameters = default,
                //materialTest = default,
                //planes = default,
                //sphereCenter = default,
                //sphereRadius = default,
                //transparentMaterialConfig = default,
                //triangleCullingConfig = default
            };
        }

        public void Build(Camera camera)
        {
            return;

            rtas.ClearInstances();
            rtas.CullInstances(ref config);

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
            //if (rtas != null)
            //    rtas.Dispose();

            rtas = null;
        }
    }
}