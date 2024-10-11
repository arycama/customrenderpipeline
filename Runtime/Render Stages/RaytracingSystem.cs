using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class RaytracingSystem : IDisposable
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField] public bool Enabled { get; private set; } = true;
            [field: SerializeField, Range(0.0f, 0.1f)] public float RaytracingBias { get; private set; } = 0.001f;
            [field: SerializeField, Range(0.0f, 0.1f)] public float RaytracingDistantBias { get; private set; } = 0.001f;
            [field: SerializeField] public LayerMask RaytracingLayers { get; private set; } = 0;
        }

        private RenderGraph renderGraph;
        private RayTracingAccelerationStructure rtas;
        private RayTracingInstanceCullingConfig config;
        private Settings settings;

        public RaytracingSystem(RenderGraph renderGraph, Settings settings)
        {
            this.settings = settings;
            this.renderGraph = renderGraph;

            RayTracingAccelerationStructure.RASSettings rasSettings = new RayTracingAccelerationStructure.RASSettings
            {
                rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                managementMode = RayTracingAccelerationStructure.ManagementMode.Manual,
                layerMask = settings.RaytracingLayers
            };
            rtas = new RayTracingAccelerationStructure(rasSettings);

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
                        layerMask = settings.RaytracingLayers,
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
            if (!settings.Enabled)
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