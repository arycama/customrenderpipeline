using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class RaytracingSystem : IDisposable
    {
        private RenderGraph renderGraph;
        private Dictionary<Camera, RayTracingAccelerationStructure> raytracingAccelerationStructures = new();
        private LayerMask layerMask;

        public RaytracingSystem(RenderGraph renderGraph, LayerMask layerMask)
        {
            this.renderGraph = renderGraph;
            this.layerMask = layerMask;
        }

        public void Build(Camera camera)
        {
            if (!raytracingAccelerationStructures.TryGetValue(camera, out var rtas))
            {
                RayTracingAccelerationStructure.RASSettings settings = new RayTracingAccelerationStructure.RASSettings();
                settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
                settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
                settings.layerMask = layerMask;
                rtas = new RayTracingAccelerationStructure(settings);
                raytracingAccelerationStructures.Add(camera, rtas);
            }

            // Calculate any data that does not change across cameras
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("RTAS Update"))
            {
                pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    command.BuildRayTracingAccelerationStructure(rtas, camera.transform.position);
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new RaytracingResult(rtas));
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
            foreach (var rtas in raytracingAccelerationStructures)
                rtas.Value.Dispose();
        }
    }
}