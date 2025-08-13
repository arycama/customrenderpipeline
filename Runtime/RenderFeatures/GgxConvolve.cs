using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class GgxConvolve : CameraRenderFeature
{
	public override string ProfilerNameOverride => "Ggx Convolve";

	private readonly Material skyMaterial, ggxConvolveMaterial;
	private readonly CustomRenderPipelineAsset settings;

	public GgxConvolve(RenderGraph renderGraph, CustomRenderPipelineAsset settings) : base(renderGraph)
	{
		ggxConvolveMaterial = new Material(Shader.Find("Hidden/GgxConvolve")) { hideFlags = HideFlags.HideAndDontSave };
        skyMaterial = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
		this.settings = settings;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		using var scope = renderGraph.AddProfileScope("Environment Probe Update");

		// TODO: Use octahedral maps
		// TODO: Init ambient probe to black or similar in the case of no skybox
		// TODO: Supply an external cubemap instead of doing all the logic here
		var ambientBuffer = renderGraph.GetBuffer(9, sizeof(float) * 4, GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination);
		//if (!RenderSettings.skybox || RenderSettings.skybox.FindPass("Cubemap") == -1)
		//{
		//	// Still need to mark buffer as written to so it gets handled correctly. Guess I could set the data here from cpu
		//	using var pass = renderGraph.AddRenderPass<GenericRenderPass>("Ambient Probe Update");
		//	pass.WriteBuffer("", ambientBuffer);

		//	renderGraph.SetResource(new EnvironmentData(renderGraph.EmptyCubemap, ambientBuffer));
		//	return;
		//}

		var envResolution = settings.LightingSettings.EnvironmentResolution;
		var reflectionProbeTemp = renderGraph.GetTexture(envResolution, envResolution, GraphicsFormat.B10G11R11_UFloatPack32, dimension: TextureDimension.Cube, hasMips: true, autoGenerateMips: true);
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Environment Cubemap"))
		{
			var keyword = string.Empty;
			var viewHeight = camera.transform.position.y;
			if (viewHeight > settings.Clouds.StartHeight)
			{
				if (viewHeight > settings.Clouds.StartHeight + settings.Clouds.LayerThickness)
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
				settings.Clouds.SetCloudPassData(pass, time);
				pass.SetFloat("_Samples", settings.Sky.ReflectionSamples);

				using var scope = ArrayPool<Matrix4x4>.Get(6, out var array);
				for (var i = 0; i < 6; i++)
				{
					var rotation = Quaternion.LookRotation(Matrix4x4Extensions.lookAtList[i], Matrix4x4Extensions.upVectorList[i]);
					var viewToWorld = Matrix4x4.TRS(Float3.Zero, rotation, Float3.One);
					array[i] = MatrixExtensions.PixelToWorldViewDirectionMatrix(envResolution, envResolution, Vector2.zero, 1.0f, 1.0f, viewToWorld, true);
				}

				pass.SetMatrixArray("_PixelToWorldViewDirs", array);
			});
		}

		var ambientComputeShader = Resources.Load<ComputeShader>("AmbientProbe");
		var ambientBufferTemp = renderGraph.GetBuffer(9, sizeof(float) * 4, GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource);
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ambient Convolve"))
		{
			pass.Initialize(ambientComputeShader, normalizedDispatch: false);
			pass.ReadTexture("_AmbientProbeInputCubemap", reflectionProbeTemp);
			pass.WriteBuffer("_AmbientProbeOutputBuffer", ambientBufferTemp);

			pass.SetRenderFunction(envResolution, static (command, pass, reflectionResolution) =>
			{
				// Prefiltered importance sampling, use lower MIP-map levels for fetching samples with low probabilities in order to reduce the variance.
				// Ref: http://http.developer.nvidia.com/GPUGems3/gpugems3_ch20.html
				// Must match compute shader
				var sampleCount = 512;

				// Solid angle associated with the texel of the cubemap
				var omegaP = Math.FourPi / (6.0f * reflectionResolution * reflectionResolution);

				// Solid angle associated with the sample
				var pdf = Math.Rcp(Math.FourPi);
				var omegaS = Math.Rcp(sampleCount * pdf);

				var mipLevel = 0.5f * Math.Log2(omegaS / omegaP);
				pass.SetFloat("_MipLevel", mipLevel);
			});
		}

		var reflectionProbe = renderGraph.GetTexture(envResolution, envResolution, GraphicsFormat.B10G11R11_UFloatPack32, 1, TextureDimension.Cube, hasMips: true);
		using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Ambient Buffer Copy"))
		{
			pass.WriteTexture(reflectionProbe);
			pass.WriteBuffer("", ambientBuffer);

			pass.ReadTexture("", reflectionProbeTemp);
			pass.ReadBuffer("", ambientBufferTemp);

			pass.SetRenderFunction((reflectionProbeTemp, ambientBufferTemp, reflectionProbe, ambientBuffer), (command, pass, data) =>
			{
				command.CopyBuffer(pass.GetBuffer(data.ambientBufferTemp), pass.GetBuffer(data.ambientBuffer));

				// Need to copy the faces one by one since we don't want to copy the whole texture
				for (var i = 0; i < 6; i++)
					command.CopyTexture(pass.GetRenderTexture(data.reflectionProbeTemp), i, 0, pass.GetRenderTexture(data.reflectionProbe), i, 0);
			});
		}

		const int mipLevels = 6;

		for (var i = 1; i < 7; i++)
		{
			using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ggx Convolve"))
			{
				pass.Initialize(ggxConvolveMaterial);
				pass.MipLevel = i;

				pass.WriteTexture(reflectionProbe);
				pass.ReadTexture("_AmbientProbeInputCubemap", reflectionProbeTemp);
				pass.SetRenderFunction((i, envResolution, settings.LightingSettings.EnvironmentSamples), static (command, pass, data) =>
				{
					using var scope = ArrayPool<Matrix4x4>.Get(6, out var array);

					for (var j = 0; j < 6; j++)
					{
						var resolution = data.envResolution >> data.i;
						var rotation = Quaternion.LookRotation(Matrix4x4Extensions.lookAtList[j], Matrix4x4Extensions.upVectorList[j]);
						var viewToWorld = Matrix4x4.TRS(Float3.Zero, rotation, Float3.One);
						array[j] = MatrixExtensions.PixelToWorldViewDirectionMatrix(resolution, resolution, Vector2.zero, 1.0f, 1.0f, viewToWorld, true);
					}

					var perceptualRoughness = Mathf.Clamp01(data.i / (float)mipLevels);
					var mipPerceptualRoughness = Mathf.Clamp01(1.7f / 1.4f - Math.Sqrt(2.89f / 1.96f - 2.8f / 1.96f * perceptualRoughness));
					var mipRoughness = mipPerceptualRoughness * mipPerceptualRoughness;

					pass.SetFloat("_Samples", data.EnvironmentSamples);
					pass.SetMatrixArray("_PixelToWorldViewDirs", array);
					pass.SetFloat("_Level", data.i);
					pass.SetFloat("_InvOmegaP", 6.0f * data.envResolution * data.envResolution / (4.0f * Mathf.PI));
					pass.SetFloat("_Roughness", mipRoughness);
				});
			}
		}

		renderGraph.SetResource(new EnvironmentData(reflectionProbe, ambientBuffer));
	}
}
