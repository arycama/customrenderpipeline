using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class GenerateHiZ : CameraRenderFeature
{
	private readonly IndexedShaderPropertyId resultIds = new("_Result");
	private readonly ComputeShader computeShader;
	private readonly HiZMode mode;

	public GenerateHiZ(RenderGraph renderGraph, HiZMode mode) : base(renderGraph)
	{
		computeShader = Resources.Load<ComputeShader>("Utility/HiZ");
		this.mode = mode;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		using var scope = renderGraph.AddProfileScope("Generate HiZ");

		var kernel = (int)mode * 2;
		var mipCount = Texture2DExtensions.MipCount(camera.scaledPixelWidth, camera.scaledPixelHeight);
		var maxMipsPerPass = 6;
		var hasSecondPass = mipCount > maxMipsPerPass;

		// Set is screen to true to get exact fit
		var result = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R32_SFloat, hasMips: true, isScreenTexture: true);

		// First pass
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Hi Z First Pass"))
		{
			pass.Initialize(computeShader, kernel, camera.scaledPixelWidth, camera.scaledPixelHeight);
			pass.ReadTexture("_Input", renderGraph.GetRTHandle<CameraDepth>());

			for (var i = 0; i < maxMipsPerPass; i++)
			{
				var texture = i < mipCount ? result : renderGraph.EmptyUavTexture;
				var mip = i < mipCount ? i : 0;
				pass.WriteTexture(resultIds.GetProperty(i), texture, mip);
			}

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetInt("_Width", camera.scaledPixelWidth);
				pass.SetInt("_Height", camera.scaledPixelHeight);
				pass.SetInt("_MaxMip", hasSecondPass ? maxMipsPerPass : mipCount);
				pass.SetVector("_InputScaleLimit", pass.GetScaleLimit2D(renderGraph.GetRTHandle<CameraDepth>()));
			});
		}

		// Second pass if needed
		if (hasSecondPass)
		{
			using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Hi Z Second Pass"))
			{
				pass.Initialize(computeShader, kernel + 1, camera.scaledPixelWidth >> (maxMipsPerPass - 1), camera.scaledPixelHeight >> (maxMipsPerPass - 1));

				for (var i = 0; i < maxMipsPerPass; i++)
				{
					var level = i + maxMipsPerPass - 1;
					var texture = level < mipCount ? result : renderGraph.EmptyUavTexture;
					var mip = level < mipCount ? level : 0;

					// Start from maxMips - 1, as we bind the last mip from the last pass as the first input for this pass
					pass.WriteTexture(resultIds.GetProperty(i), texture, mip);
				}

				pass.SetRenderFunction((command, pass) =>
				{
					pass.SetInt("_Width", camera.scaledPixelWidth >> (maxMipsPerPass - 1));
					pass.SetInt("_Height", camera.scaledPixelHeight >> (maxMipsPerPass - 1));
					pass.SetInt("_MaxMip", mipCount - maxMipsPerPass);
				});
			}
		}

		if(mode == HiZMode.Min)
			renderGraph.SetRTHandle<HiZMinDepth>(result);
		else if (mode == HiZMode.Max)
			renderGraph.SetRTHandle<HiZMaxDepth>(result);
	}

	public enum HiZMode
	{
		Min,
		Max,
		CheckerMinMax
	}
}
