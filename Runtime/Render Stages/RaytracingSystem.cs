using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class RaytracingSystem : RenderFeature
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField] public bool Enabled { get; private set; } = true;
            [field: SerializeField, Range(0.0f, 0.1f)] public float RaytracingBias { get; private set; } = 0.001f;
            [field: SerializeField, Range(0.0f, 0.1f)] public float RaytracingDistantBias { get; private set; } = 0.001f;
            [field: SerializeField] public LayerMask RaytracingLayers { get; private set; } = 0;
        }

        private RayTracingAccelerationStructure rtas;
        private RayTracingInstanceCullingConfig config;
        private readonly Settings settings;

        public RaytracingSystem(RenderGraph renderGraph, Settings settings) : base(renderGraph)
        {
            this.settings = settings;

            var rasSettings = new RayTracingAccelerationStructure.RASSettings
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

        public override void Render()
        {
            if (!settings.Enabled)
                return;

            rtas.ClearInstances();
            _ = rtas.CullInstances(ref config);

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("RTAS Update"))
            {
                pass.SetRenderFunction(rtas, (command, pass, data) =>
                {
                    command.BuildRayTracingAccelerationStructure(data);
                });
            }

            renderGraph.SetResource(new RaytracingResult(rtas));;
        }

        protected override void Cleanup(bool disposing)
        {
            // Disposing seems to crash for some reason, maybe only from a destructor?
            if (renderGraph.RenderPipeline.IsDisposingFromRenderDoc)
                return;

            //if (rtas != null)
            //    rtas.Dispose();

            rtas = null;
        }
    }
}