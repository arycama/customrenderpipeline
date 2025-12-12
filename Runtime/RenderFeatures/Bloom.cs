using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

public class Bloom : ViewRenderFeature
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField, Range(0f, 1f)] public float BloomStrength { get; private set; } = 0.125f;
		[field: SerializeField, Range(2, 12)] public int MaxMips { get; private set; } = 6;
		[field: SerializeField] public Texture2D LensDirt { get; private set; }
		[field: SerializeField, Range(0f, 1f)] public float DirtStrength { get; private set; } = 0.04f;

		[field: Header("Lens Flare")]
		[field: SerializeField, Range(0, 10)] public int FlareMip { get; private set; } = 2;

		[field: SerializeField, Range(1, 3)] public int DistortionQuality { get; private set; } = 2;
		[field: SerializeField, Range(0f, 0.1f)] public float Distortion { get; private set; } = 0.02f;

		[field: SerializeField, Range(0f, 0.1f)] public float GhostStrength { get; private set; } = 1f;
		[field: SerializeField, Range(0, 8)] public int GhostCount { get; private set; } = 7;
		[field: SerializeField, Range(0f, 1f)] public float GhostSpacing { get; private set; } = 1.19f;

		[field: SerializeField, Range(0f, 0.1f)] public float HaloStrength { get; private set; } = 1f;
		[field: SerializeField, Range(0f, 1f)] public float HaloRadius { get; private set; } = 0.692f;
		[field: SerializeField, Range(0f, 1f)] public float HaloWidth { get; private set; } = 0.692f;

		[field: SerializeField, Range(0f, 1f)] public float StreakStrength { get; private set; } = 1f;
		[field: SerializeField] public Texture2D StarburstTexture { get; private set; }
	}

	private static readonly int LensDirtId = Shader.PropertyToID("LensDirt");
	private static readonly int StarBurstId = Shader.PropertyToID("StarBurst");
	
	private readonly Settings settings;
	private readonly Material material;

	public Bloom(RenderGraph renderGraph, Settings settings) : base(renderGraph)
	{
		this.settings = settings;
		material = new Material(Shader.Find("Hidden/Bloom")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		renderGraph.AddProfileBeginPass("Bloom");

		var bloomIds = ListPool<ResourceHandle<RenderTexture>>.Get();

		// Need to queue up all the textures first
		var mipCount = Mathf.Min(settings.MaxMips, (int)Mathf.Log(Mathf.Max(viewRenderData.viewSize.x, viewRenderData.viewSize.y), 2));
		for (var i = 0; i < mipCount; i++)
		{
			var width = Mathf.Max(1, viewRenderData.viewSize.x >> (i + 1));
			var height = Mathf.Max(1, viewRenderData.viewSize.y >> (i + 1));

			var resultId = renderGraph.GetTexture(new(width, height), GraphicsFormat.B10G11R11_UFloatPack32);
			bloomIds.Add(resultId);
		}

		// Downsample
		for (var i = 0; i < mipCount; i++)
		{
			var width = Mathf.Max(1, viewRenderData.viewSize.x >> (i + 1));
			var height = Mathf.Max(1, viewRenderData.viewSize.y >> (i + 1));

			var source = i > 0 ? bloomIds[i - 1] : renderGraph.GetRTHandle<CameraTarget>();

			using var pass = renderGraph.AddFullscreenRenderPass("Bloom Down", new BloomData
			(
				new Float2(1.0f / width, 1.0f / height),
				source,
				settings.DirtStrength,
				settings.LensDirt,
				settings.DistortionQuality,
				settings.Distortion,
				settings.GhostStrength,
				settings.GhostCount,
				settings.GhostSpacing,
				settings.HaloStrength,
				settings.HaloWidth,
				settings.HaloRadius,
				settings.StreakStrength,
				settings.StarburstTexture,
				settings.BloomStrength
			));

			pass.Initialize(material, i == settings.FlareMip ? 0 : 1);
			pass.WriteTexture(bloomIds[i], RenderBufferLoadAction.DontCare);

			pass.ReadTexture("Input", source);
			pass.ReadResource<ViewData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetVector("RcpResolution", data.RcpResolution);
				pass.SetVector("InputScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.source));
				pass.SetFloat("DirtStrength", data.DirtStrength);

				if (data.LensDirt != null)
					pass.SetTexture(LensDirtId, data.LensDirt);

				// Lens flare 
				pass.SetFloat("DistortionQuality", data.DistortionQuality);
				pass.SetFloat("Distortion", data.Distortion);
				pass.SetFloat("GhostStrength", data.GhostStrength);
				pass.SetFloat("GhostCount", data.GhostCount);
				pass.SetFloat("GhostSpacing", data.GhostSpacing);
				pass.SetFloat("HaloStrength", data.HaloStrength);
				pass.SetFloat("HaloWidth", data.HaloWidth);
				pass.SetFloat("HaloRadius", data.HaloRadius);
				pass.SetFloat("StreakStrength", data.StreakStrength);

				if (data.StarburstTexture != null)
					pass.SetTexture(StarBurstId, data.StarburstTexture);
			});
		}

		// Upsample
		for (var i = mipCount - 1; i > 0; i--)
		{
			var input = bloomIds[i];
			var width = Mathf.Max(1, viewRenderData.viewSize.x >> i);
			var height = Mathf.Max(1, viewRenderData.viewSize.y >> i);

			using var pass = renderGraph.AddFullscreenRenderPass("Bloom Up", new BloomData
			(
				new Float2(1.0f / width, 1.0f / height),
				input,
				settings.DirtStrength,
				settings.LensDirt,
				settings.DistortionQuality,
				settings.Distortion,
				settings.GhostStrength,
				settings.GhostCount,
				settings.GhostSpacing,
				settings.HaloStrength,
				settings.HaloWidth,
				settings.HaloRadius,
				settings.StreakStrength,
				settings.StarburstTexture,
				settings.BloomStrength
			));

			pass.Initialize(material, 2);
			pass.WriteTexture(bloomIds[i - 1]);
			pass.ReadTexture("Input", input);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("Strength", data.bloomStrength);
				pass.SetVector("RcpResolution", data.RcpResolution);
				pass.SetVector("InputScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.source));

				pass.SetFloat("DirtStrength", data.DirtStrength);

				if (data.LensDirt != null)
					pass.SetTexture(LensDirtId, data.LensDirt);

				// Lens flare 
				pass.SetFloat("DistortionQuality", data.DistortionQuality);
				pass.SetFloat("Distortion", data.Distortion);
				pass.SetFloat("GhostStrength", data.GhostStrength);
				pass.SetFloat("GhostCount", data.GhostCount);
				pass.SetFloat("GhostSpacing", data.GhostSpacing);
				pass.SetFloat("HaloStrength", data.HaloStrength);
				pass.SetFloat("HaloWidth", data.HaloWidth);
				pass.SetFloat("HaloRadius", data.HaloRadius);
				pass.SetFloat("StreakStrength", data.StreakStrength);

				if (data.StarburstTexture != null)
					pass.SetTexture(StarBurstId, data.StarburstTexture);
			});
		}

		renderGraph.SetRTHandle<CameraBloom>(bloomIds[0]);
		ListPool<ResourceHandle<RenderTexture>>.Release(bloomIds);

		renderGraph.AddProfileEndPass("Bloom");
	}

	private readonly struct BloomData
	{
		public readonly Float2 RcpResolution;
		public readonly ResourceHandle<RenderTexture> source;
		public readonly float DirtStrength;
		public readonly Texture2D LensDirt;
		public readonly int DistortionQuality;
		public readonly float Distortion;
		public readonly float GhostStrength;
		public readonly int GhostCount;
		public readonly float GhostSpacing;
		public readonly float HaloStrength;
		public readonly float HaloWidth;
		public readonly float HaloRadius;
		public readonly float StreakStrength;
		public readonly Texture2D StarburstTexture;
		public readonly float bloomStrength;

		public BloomData(Float2 rcpResolution, ResourceHandle<RenderTexture> source, float dirtStrength, Texture2D lensDirt, int distortionQuality, float distortion, float ghostStrength, int ghostCount, float ghostSpacing, float haloStrength, float haloWidth, float haloRadius, float streakStrength, Texture2D starburstTexture, float bloomStrength)
		{
			RcpResolution = rcpResolution;
			this.source = source;
			DirtStrength = dirtStrength;
			LensDirt = lensDirt;
			DistortionQuality = distortionQuality;
			Distortion = distortion;
			GhostStrength = ghostStrength;
			GhostCount = ghostCount;
			GhostSpacing = ghostSpacing;
			HaloStrength = haloStrength;
			HaloWidth = haloWidth;
			HaloRadius = haloRadius;
			StreakStrength = streakStrength;
			StarburstTexture = starburstTexture;
			this.bloomStrength = bloomStrength;
		}
	}
}