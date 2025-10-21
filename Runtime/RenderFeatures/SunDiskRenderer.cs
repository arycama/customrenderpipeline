using UnityEngine;
using UnityEngine.Rendering;

public class SunDiskRenderer : CameraRenderFeature
{
	private readonly LightingSettings settings;
	private readonly Material celestialBodyMaterial;

	public SunDiskRenderer(RenderGraph renderGraph, LightingSettings settings) : base(renderGraph)
	{
		this.settings = settings;
		celestialBodyMaterial = new Material(Shader.Find("Surface/Celestial Body")){ hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var lightData = renderGraph.GetResource<LightingData>();
		var viewPosition = camera.transform.position;
		var scale = 2 * Mathf.Tan(0.5f * settings.SunAngularDiameter * Mathf.Deg2Rad);
		var matrix = Matrix4x4.TRS(viewPosition + lightData.light0Direction, Quaternion.LookRotation(lightData.light0Direction), Vector3.one * scale);

		using (var pass = renderGraph.AddDrawProceduralRenderPass("Sun Disk", (settings.SunAngularDiameter, lightData.light0Color, lightData.light0Direction)))
		{
			pass.Initialize(celestialBodyMaterial, matrix, 0, 4, 1, MeshTopology.Quads);

			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());

			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<ViewData>();

			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<TemporalAAData>();

			pass.AddRenderPassData<SkyTransmittanceData>();
			pass.AddRenderPassData<CloudShadowDataResult>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("AngularDiameter", data.SunAngularDiameter);
				pass.SetVector("Luminance", data.light0Color);
				pass.SetVector("Direction", data.light0Direction);
			});
		}
	}
}
