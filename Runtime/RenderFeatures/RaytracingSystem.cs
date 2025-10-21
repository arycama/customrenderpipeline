using System;
using UnityEngine;

using UnityEngine.Rendering;

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

		var rasSettings = new RayTracingAccelerationStructure.Settings(RayTracingAccelerationStructure.ManagementMode.Manual, RayTracingAccelerationStructure.RayTracingModeMask.Everything, settings.RaytracingLayers);

        rtas = new RayTracingAccelerationStructure(rasSettings);
		var config = new RayTracingInstanceCullingConfig
		{
			flags = RayTracingInstanceCullingFlags.None,
			subMeshFlagsConfig = new RayTracingSubMeshFlagsConfig
			{
				opaqueMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly,
				alphaTestedMaterials = RayTracingSubMeshFlags.Enabled,
				transparentMaterials = RayTracingSubMeshFlags.Disabled,
			},

			instanceTests = new RayTracingInstanceCullingTest[]
			{
				new()
				{
                    allowOpaqueMaterials = true,
                    allowAlphaTestedMaterials = true,
                    allowTransparentMaterials = false, // TODO: Support?
					layerMask = settings.RaytracingLayers,
					shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided),
					instanceMask = 1
				}
			},

			alphaTestedMaterialConfig = new RayTracingInstanceMaterialConfig
			{
				renderQueueLowerBound = (int)RenderQueue.AlphaTest,
				renderQueueUpperBound = (int)RenderQueue.GeometryLast,
				//optionalShaderKeywords = new string[1] { "CUTOUT_ON" },
			}
		};

		rtas.ClearInstances();
		_ = rtas.CullInstances(ref config);
	}

	public override void Render(ScriptableRenderContext context)
    {
        if (!settings.Enabled)
            return;

		// TODO: Could use camera relative, 1 rtas per camera
        using (var pass = renderGraph.AddGenericRenderPass("RTAS Update", rtas))
        {
            pass.SetRenderFunction(static (command, pass, data) =>
            {
                command.BuildRayTracingAccelerationStructure(data);
            });
        }

        renderGraph.SetResource(new RaytracingResult(rtas, settings.RaytracingBias, settings.RaytracingDistantBias));
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
