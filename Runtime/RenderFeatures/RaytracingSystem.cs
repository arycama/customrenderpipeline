using System;
using System.Reflection;
using UnityEngine;

using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    public class RaytracingSystem : FrameRenderFeature
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
        private readonly Settings settings;

        public RaytracingSystem(RenderGraph renderGraph, Settings settings) : base(renderGraph)
        {
            this.settings = settings;

        }

        public override void Render(ScriptableRenderContext context)
        {
            if (!settings.Enabled)
                return;

            if (rtas == null)
            {
                var rasSettings = new RayTracingAccelerationStructure.Settings(RayTracingAccelerationStructure.ManagementMode.Automatic, RayTracingAccelerationStructure.RayTracingModeMask.Everything, settings.RaytracingLayers);
                rtas = new RayTracingAccelerationStructure(rasSettings);
            }

            // TODO: Could use camera relative, 1 rtas per camera
            using (var pass = renderGraph.AddGenericRenderPass("RTAS Update", (rtas, context)))
            {
                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    var field = typeof(RayTracingAccelerationStructure).GetField("m_Ptr", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (data.rtas != null && (IntPtr)field.GetValue(data.rtas) != IntPtr.Zero)
                    {
                        command.BuildRayTracingAccelerationStructure(data.rtas);
                        data.context.ExecuteCommandBuffer(command);
                        command.Clear();
                    }
                });
            }

            renderGraph.SetResource(new RaytracingResult(rtas, settings.RaytracingBias, settings.RaytracingDistantBias));
        }

        protected override void Cleanup(bool disposing)
        {
            // Disposing seems to crash for some reason, maybe only from a destructor?
            if (renderGraph.RenderPipeline.IsDisposingFromRenderDoc)
                return;

            if (rtas != null)
            {
                //rtas.Release();
                rtas = null;
            }
        }
    }

    /// <summary>
    /// Flags returned when trying to add a renderer into the ray tracing acceleration structure.
    /// </summary>
    public enum AccelerationStructureStatus
    {
        /// <summary>Initial flag state.</summary>
        Clear = 0x0,
        /// <summary>Flag that indicates that the renderer was successfully added to the ray tracing acceleration structure.</summary>
        Added = 0x1,
        /// <summary>Flag that indicates that the renderer was excluded from the ray tracing acceleration structure.</summary>
        Excluded = 0x02,
        /// <summary>Flag that indicates that the renderer was added to the ray tracing acceleration structure, but it had transparent and opaque sub-meshes.</summary>
        TransparencyIssue = 0x04,
        /// <summary>Flag that indicates that the renderer was not included into the ray tracing acceleration structure because of a missing material</summary>
        NullMaterial = 0x08,
        /// <summary>Flag that indicates that the renderer was not included into the ray tracing acceleration structure because of a missing mesh</summary>
        MissingMesh = 0x10
    }
}