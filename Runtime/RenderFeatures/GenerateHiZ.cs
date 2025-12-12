using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class GenerateHiZ : ViewRenderFeature
{
	private readonly IndexedShaderPropertyId resultIds = new("_Result");
	private readonly ComputeShader computeShader;
	private readonly HiZMode mode;

	public GenerateHiZ(RenderGraph renderGraph, HiZMode mode) : base(renderGraph)
	{
		computeShader = Resources.Load<ComputeShader>("Utility/HiZ");
		this.mode = mode;
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		using var scope = renderGraph.AddProfileScope("Generate HiZ");

		var kernel = (int)mode * 2;
		var mipCount = Texture2DExtensions.MipCount(viewRenderData.viewSize);
		var maxMipsPerPass = 6;
		var hasSecondPass = mipCount > maxMipsPerPass;

		// Set is screen to true to get exact fit
		var result = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.R32_SFloat, hasMips: true, isScreenTexture: true);

		// First pass
		using (var pass = renderGraph.AddComputeRenderPass("Hi Z First Pass", (viewRenderData.viewSize, hasSecondPass ? maxMipsPerPass : mipCount, renderGraph.GetRTHandle<CameraDepth>())))
		{
			pass.Initialize(computeShader, kernel, viewRenderData.viewSize.x, viewRenderData.viewSize.y);
			pass.ReadTexture("_Input", renderGraph.GetRTHandle<CameraDepth>());

			for (var i = 0; i < maxMipsPerPass; i++)
			{
				var texture = i < mipCount ? result : renderGraph.EmptyUavTexture;
				var mip = i < mipCount ? i : 0;
				pass.WriteTexture(resultIds.GetProperty(i), texture, mip);
			}

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetInt("_Width", data.Item1.x);
				pass.SetInt("_Height", data.Item1.y);
				pass.SetInt("_MaxMip", data.Item2);
				pass.SetVector("_InputScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.Item3));
			});
		}

		// Second pass if needed
		if (hasSecondPass)
		{
			using (var pass = renderGraph.AddComputeRenderPass("Hi Z Second Pass", (viewRenderData.viewSize, maxMipsPerPass, mipCount)))
			{
				pass.Initialize(computeShader, kernel + 1, viewRenderData.viewSize.x >> (maxMipsPerPass - 1), viewRenderData.viewSize.y >> (maxMipsPerPass - 1));

				for (var i = 0; i < maxMipsPerPass; i++)
				{
					var level = i + maxMipsPerPass - 1;
					var texture = level < mipCount ? result : renderGraph.EmptyUavTexture;
					var mip = level < mipCount ? level : 0;

					// Start from maxMips - 1, as we bind the last mip from the last pass as the first input for this pass
					pass.WriteTexture(resultIds.GetProperty(i), texture, mip);
				}

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					pass.SetInt("_Width", data.Item1.x >> (data.maxMipsPerPass - 1));
					pass.SetInt("_Height", data.Item1.y >> (data.maxMipsPerPass - 1));
					pass.SetInt("_MaxMip", data.mipCount - data.maxMipsPerPass);
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
