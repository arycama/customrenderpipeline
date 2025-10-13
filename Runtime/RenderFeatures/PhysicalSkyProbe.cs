using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class PhysicalSkyProbe : CameraRenderFeature
{
	public override string ProfilerNameOverride => "Ggx Convolve";

	private readonly Material skyMaterial;
	private readonly EnvironmentLightingSettings environmentLighting;
	private readonly VolumetricClouds.Settings cloudSettings;
	private readonly Sky.Settings skySettings;

	public PhysicalSkyProbe(RenderGraph renderGraph, EnvironmentLightingSettings environmentLighting, VolumetricClouds.Settings cloudSettings, Sky.Settings skySettings) : base(renderGraph)
	{
        skyMaterial = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
		this.environmentLighting = environmentLighting;
		this.cloudSettings = cloudSettings;
		this.skySettings = skySettings;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		using var scope = renderGraph.AddProfileScope("Environment Probe Update");

		var reflectionProbeTemp = renderGraph.GetTexture(environmentLighting.Resolution, environmentLighting.Resolution, GraphicsFormat.B10G11R11_UFloatPack32, dimension: TextureDimension.Cube, hasMips: true, autoGenerateMips: true);
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Environment Cubemap"))
		{
			var keyword = string.Empty;
			var viewHeight = camera.transform.position.y;
			if (viewHeight > cloudSettings.StartHeight)
			{
				if (viewHeight > cloudSettings.StartHeight + cloudSettings.LayerThickness)
				{
					keyword = "ABOVE_CLOUD_LAYER";
				}
			}
			else
			{
				keyword = "BELOW_CLOUD_LAYER";
			}

			pass.Initialize(skyMaterial, skyMaterial.FindPass("Reflection Probe"), 1, keyword);
			pass.WriteTexture(reflectionProbeTemp);

			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<CloudData>();
			pass.AddRenderPassData<LightingData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<SkyTransmittanceData>();
			pass.AddRenderPassData<SkyReflectionAmbientData>();
			
			var time = (float)pass.RenderGraph.GetResource<TimeData>().Time;

			pass.SetRenderFunction((command, pass) =>
			{
				cloudSettings.SetCloudPassData(pass, time);
				pass.SetFloat("_Samples", skySettings.ReflectionSamples);

				using var scope = ArrayPool<Matrix4x4>.Get(6, out var array);
				for (var i = 0; i < 6; i++)
				{
					var rotation = Quaternion.LookRotation(Matrix4x4Extensions.lookAtList[i], Matrix4x4Extensions.upVectorList[i]);
					var viewToWorld = Matrix4x4.TRS(Float3.Zero, rotation, Float3.One);
					array[i] = MatrixExtensions.PixelToWorldViewDirectionMatrix(environmentLighting.Resolution, environmentLighting.Resolution, Vector2.zero, 1.0f, 1.0f, viewToWorld, true);
				}

				pass.SetMatrixArray("_PixelToWorldViewDirs", array);
			});
		}

		renderGraph.SetResource(new EnvironmentProbeTempResult(reflectionProbeTemp));
	}
}
