using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class WaterCaustics : ViewRenderFeature
{
	private readonly WaterSettings settings;
	private readonly Material material;
	private readonly ResourceHandle<GraphicsBuffer> indexBuffer;

	public WaterCaustics(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph)
	{
		this.settings = settings;
		material = new Material(Shader.Find("Hidden/Water Caustics")) { hideFlags = HideFlags.HideAndDontSave };
		indexBuffer = renderGraph.GetGridIndexBuffer(128, false, false);
	}

	protected override void Cleanup(bool disposing)
	{
		renderGraph.ReleasePersistentResource(indexBuffer, -1);
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		if (!settings.IsEnabled)
			return;

		using var scope = renderGraph.AddProfileScope("Water Caustics");

		var Profile = settings.Profile;
		var patchSizes = new Vector4(Profile.PatchSize / Math.Pow(Profile.CascadeScale, 0f), Profile.PatchSize / Math.Pow(Profile.CascadeScale, 1f), Profile.PatchSize / Math.Pow(Profile.CascadeScale, 2f), Profile.PatchSize / Math.Pow(Profile.CascadeScale, 3f));
		var patchSize = patchSizes[settings.CasuticsCascade];

		var temp0 = renderGraph.GetTexture(129, GraphicsFormat.R16G16B16A16_SFloat, isExactSize: true);
		using (var pass = renderGraph.AddFullscreenRenderPass("Ocean Caustics Prepare", (settings.CausticsDepth, settings.CasuticsCascade, patchSize)))
		{
			pass.Initialize(material, 2);
			pass.WriteTexture(temp0, RenderBufferLoadAction.DontCare);
			pass.ReadResource<LightingData>();
			pass.ReadResource<OceanFftResult>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("_CausticsDepth", data.CausticsDepth);
				pass.SetFloat("_CausticsCascade", data.CasuticsCascade);
				pass.SetFloat("_PatchSize", data.patchSize);
				pass.SetVector("_RefractiveIndex", Float3.One * (1.0f / 1.34f));
			});
		}

		var tempResult = renderGraph.GetTexture(settings.CasuticsResolution * 2, GraphicsFormat.B10G11R11_UFloatPack32, isExactSize: true, clearFlags: RTClearFlags.Color);
		using (var pass = renderGraph.AddDrawProceduralIndexedRenderPass("Ocean Caustics Render", (patchSize, settings.CausticsDepth, settings.CasuticsCascade)))
		{
			pass.Initialize(indexBuffer, material, Matrix4x4.identity, 0);

			pass.WriteTexture(tempResult);

			pass.ReadResource<LightingData>();
			pass.ReadResource<OceanFftResult>();
			pass.ReadTexture("_Input", temp0);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				var viewMatrix = Matrix4x4.LookAt(Vector3.zero, Vector3.down, Vector3.forward).inverse;
				var projectionMatrix = Matrix4x4.Ortho(-data.patchSize, data.patchSize, -data.patchSize, data.patchSize, 0, data.CausticsDepth * 2);
				command.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

				pass.SetFloat("_CausticsDepth", data.CausticsDepth);
				pass.SetFloat("_CausticsCascade", data.CasuticsCascade);
				pass.SetFloat("_PatchSize", data.patchSize);
				pass.SetVector("_RefractiveIndex", Float3.One * (1.0f / 1.34f));
			});
		}

		var result = renderGraph.GetTexture(settings.CasuticsResolution, GraphicsFormat.B10G11R11_UFloatPack32, hasMips: true, autoGenerateMips: true, isExactSize: true);
		using (var pass = renderGraph.AddFullscreenRenderPass("Ocean Caustics Blit"))
		{
			pass.Initialize(material, 1);
			pass.ReadTexture("_MainTex", tempResult);
			pass.WriteTexture(result, RenderBufferLoadAction.DontCare);
		}

		renderGraph.SetResource(new CausticsResult(result, settings.CasuticsCascade, settings.CausticsDepth));
	}
}