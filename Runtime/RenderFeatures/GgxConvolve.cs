using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class EnvironmentConvolve : CameraRenderFeature
{
	public override string ProfilerNameOverride => "Ggx Convolve";

	private readonly Material convolveMaterial;
	private readonly EnvironmentLightingSettings settings;

	public EnvironmentConvolve(RenderGraph renderGraph, EnvironmentLightingSettings settings) : base(renderGraph)
	{
		convolveMaterial = new Material(Shader.Find("Hidden/GgxConvolve")) { hideFlags = HideFlags.HideAndDontSave };
		this.settings = settings;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		using var scope = renderGraph.AddProfileScope("Environment Probe Convolve");

		var ambientComputeShader = Resources.Load<ComputeShader>("AmbientProbe");
		var ambientBufferTemp = renderGraph.GetBuffer(7, sizeof(float) * 4, GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource);
		using (var pass = renderGraph.AddComputeRenderPass("Ambient Convolve", settings.Resolution))
		{
			pass.Initialize(ambientComputeShader, normalizedDispatch: false);
			pass.WriteBuffer("_AmbientProbeOutputBuffer", ambientBufferTemp);
			pass.AddRenderPassData<EnvironmentProbeTempResult>();

			pass.SetRenderFunction(static (command, pass, reflectionResolution) =>
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

		var reflectionProbe = renderGraph.GetTexture(settings.Resolution, settings.Resolution, GraphicsFormat.B10G11R11_UFloatPack32, hasMips: true, isExactSize: true);
		var ambientBuffer = renderGraph.GetBuffer(7, sizeof(float) * 4, GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination);
		var reflectionProbeTemp = renderGraph.GetResource<EnvironmentProbeTempResult>();

		using (var pass = renderGraph.AddGenericRenderPass("Ambient Buffer Copy", (reflectionProbeTemp.TempProbe, ambientBufferTemp, reflectionProbe, ambientBuffer)))
		{
			pass.WriteTexture(reflectionProbe);
			pass.WriteBuffer("", ambientBuffer);
			pass.ReadBuffer("", ambientBufferTemp);
			pass.AddRenderPassData<EnvironmentProbeTempResult>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				command.CopyBuffer(pass.GetBuffer(data.ambientBufferTemp), pass.GetBuffer(data.ambientBuffer));
				command.CopyTexture(pass.GetRenderTexture(data.TempProbe), 0, 0, pass.GetRenderTexture(data.reflectionProbe), 0, 0);
			});
		}

		const int mipLevels = 6;
		for (var i = 1; i < 7; i++)
		{
			using (var pass = renderGraph.AddFullscreenRenderPass("Ggx Convolve", (i, envResolution: settings.Resolution, settings.Samples)))
			{
				pass.Initialize(convolveMaterial);
				pass.MipLevel = i;

				pass.WriteTexture(reflectionProbe);
				pass.AddRenderPassData<EnvironmentProbeTempResult>();

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					var perceptualRoughness = Math.Saturate(data.i / (float)mipLevels);
					var mipPerceptualRoughness = Math.Saturate(1.7f / 1.4f - Math.Sqrt(2.89f / 1.96f - 2.8f / 1.96f * perceptualRoughness));
					var mipRoughness = mipPerceptualRoughness * mipPerceptualRoughness;

					pass.SetInt("Samples", data.Samples);
					pass.SetFloat("RcpSamples", Math.Rcp(data.Samples));
					pass.SetFloat("Level", data.i);
					pass.SetFloat("RcpOmegaP", data.envResolution * data.envResolution / (4.0f * Math.Pi * data.Samples));
					pass.SetFloat("PerceptualRoughness", mipPerceptualRoughness);
					pass.SetFloat("Roughness", mipRoughness);
				});
			}
		}

		renderGraph.SetResource(new EnvironmentData(reflectionProbe, ambientBuffer));
	}
}
