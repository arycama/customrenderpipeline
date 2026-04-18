using UnityEngine;

public class SunDiskRenderer : ViewRenderFeature
{
	private readonly LightingSettings settings;
	private readonly Material celestialBodyMaterial;

	public SunDiskRenderer(RenderGraph renderGraph, LightingSettings settings) : base(renderGraph)
	{
		this.settings = settings;
		celestialBodyMaterial = new Material(Shader.Find("Surface/Celestial Body")){ hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		var lightData = renderGraph.GetResource<LightingData>();
		var viewPosition = viewRenderData.transform.position;
		var scale = 2 * Math.Tan(0.5f * Math.Radians(settings.SunAngularDiameter));

        var matrix = Float4x4.TRS(viewPosition - lightData.light0Rotation.Forward, lightData.light0Rotation.ReverseForward, scale);
		using (var pass = renderGraph.AddDrawProceduralRenderPass("Sun Disk", (settings.SunAngularDiameter, lightData.light0Color, -lightData.light0Rotation.Forward)))
		{
			pass.Initialize(celestialBodyMaterial, matrix, viewRenderData.viewSize, 1, 0, 4, 1, MeshTopology.Quads);

			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());

			pass.ReadResource<AutoExposureData>();
			pass.ReadResource<ViewData>();

			pass.ReadResource<AtmospherePropertiesAndTables>();
			pass.ReadResource<TemporalAAData>();

			pass.ReadResource<SkyViewTransmittanceData>();
			pass.ReadResource<CloudShadowDataResult>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("AngularDiameter", data.SunAngularDiameter);
				pass.SetVector("Illuminance", data.light0Color);
				pass.SetVector("Direction", data.Item3);
			});
		}
	}
}
