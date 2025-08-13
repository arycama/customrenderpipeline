using System.Collections;
using System.Collections.Generic;
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
		using (var pass = renderGraph.AddRenderPass<DrawProceduralRenderPass>("Sun Disk"))
		{
			var lightData = renderGraph.GetResource<LightingData>();
			var viewPosition = camera.transform.position;
			var scale = 2 * Mathf.Tan(0.5f * settings.SunAngularDiameter * Mathf.Deg2Rad);
			var matrix = Matrix4x4.TRS(viewPosition + lightData.Light0Direction, Quaternion.LookRotation(lightData.Light0Direction), Vector3.one * scale);

			pass.Initialize(celestialBodyMaterial, matrix, 0, 4, 1, MeshTopology.Quads);

			var depth = renderGraph.GetResource<CameraDepthData>().Handle;
			pass.WriteDepth(depth);
			pass.WriteTexture(renderGraph.GetResource<CameraTargetData>().Handle);

			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<ViewData>();

			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<TemporalAAData>();

			pass.AddRenderPassData<SkyTransmittanceData>();
			pass.AddRenderPassData<SkyResultData>();
			pass.AddRenderPassData<CloudRenderResult>();
			pass.AddRenderPassData<CloudShadowDataResult>();

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetFloat("AngularDiameter", settings.SunAngularDiameter);
				pass.SetVector("Luminance", (Vector3)lightData.Light0Color);
				pass.SetVector("Direction", (Vector3)lightData.Light0Direction);
			});
		}
	}
}
