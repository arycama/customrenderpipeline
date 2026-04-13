using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;

public class Bloom : ViewRenderFeature
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField, Range(0f, 1f)] public float Strength { get; private set; } = 0.125f;
		[field: SerializeField, Range(1, 12)] public int MaxMips { get; private set; } = 6;
	}

	private readonly Settings settings;
	private readonly Material material;

	public Bloom(RenderGraph renderGraph, Settings settings) : base(renderGraph)
	{
		this.settings = settings;
		material = new Material(Shader.Find("Hidden/Bloom")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(ViewRenderData viewRenderData)
    {
        if (settings.Strength == 0)
            return;

		renderGraph.AddProfileBeginPass("Bloom");

		var bloomIds = ListPool<ResourceHandle<RenderTexture>>.Get();
		var mipCount = Math.Min(settings.MaxMips, (int)Math.Log2(Math.Max(viewRenderData.viewSize.x, viewRenderData.viewSize.y)));

		// Downsample
		for (var i = 0; i < mipCount; i++)
		{
			var width = Math.Max(1, viewRenderData.viewSize.x >> (i + 1));
			var height = Math.Max(1, viewRenderData.viewSize.y >> (i + 1));

            var dest = renderGraph.GetTexture(new(width, height), GraphicsFormat.B10G11R11_UFloatPack32, isExactSize: true);
            bloomIds.Add(dest);

            var source = i > 0 ? bloomIds[i - 1] : renderGraph.GetRTHandle<CameraTarget>();

            using var pass = renderGraph.AddFullscreenRenderPass("Bloom Down",(1.0f / new Float2(width, height), source));
            pass.UseProfiler = false;

            pass.Initialize(material, new(width, height), viewRenderData.viewCount, i == 0 ? 0 : 1);
			pass.WriteTexture(dest);

			pass.ReadTexture("Input", source);
			pass.ReadResource<ViewData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetVector("RcpResolution", data.Item1);
				pass.SetVector("InputScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.source));
			});
		}

		// Upsample
		for (var i = mipCount - 1; i > 0; i--)
		{
			var input = bloomIds[i];
			var width = Math.Max(1, viewRenderData.viewSize.x >> i);
			var height = Math.Max(1, viewRenderData.viewSize.y >> i);

			using var pass = renderGraph.AddFullscreenRenderPass("Bloom Up", (1.0f / new Float2(width, height), input, settings.Strength));
            pass.UseProfiler = false;

			pass.Initialize(material, new(width, height), viewRenderData.viewCount, 2);
			pass.WriteTexture(bloomIds[i - 1]);
			pass.ReadTexture("Input", input);

			pass.SetRenderFunction((static (command, pass, data) =>
			{
				pass.SetFloat("Strength", data.Strength);
				pass.SetVector("RcpResolution", data.Item1);
				pass.SetVector("InputScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.Item2));
			}));
		}

		renderGraph.SetRTHandle<CameraBloom>(bloomIds[0]);
		ListPool<ResourceHandle<RenderTexture>>.Release(bloomIds);

		renderGraph.AddProfileEndPass("Bloom");
	}
}