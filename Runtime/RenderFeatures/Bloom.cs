using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

public class Bloom : CameraRenderFeature
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

	private Settings settings;
	private Material material;

	public Bloom(RenderGraph renderGraph, Settings settings) : base(renderGraph)
	{
		this.settings = settings;
		material = new Material(Shader.Find("Hidden/Bloom")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		renderGraph.AddProfileBeginPass("Bloom");

		var bloomIds = ListPool<ResourceHandle<RenderTexture>>.Get();

		// Need to queue up all the textures first
		var mipCount = Mathf.Min(settings.MaxMips, (int)Mathf.Log(Mathf.Max(camera.pixelWidth, camera.pixelHeight), 2));
		for (var i = 0; i < mipCount; i++)
		{
			var width = Mathf.Max(1, camera.pixelWidth >> (i + 1));
			var height = Mathf.Max(1, camera.pixelHeight >> (i + 1));

			var resultId = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32);
			bloomIds.Add(resultId);
		}

		// Downsample
		for (var i = 0; i < mipCount; i++)
		{
			using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Bloom Down");
			pass.Initialize(material, i == settings.FlareMip ? 0 : 1);
			pass.WriteTexture(bloomIds[i], RenderBufferLoadAction.DontCare);

			var rt = i > 0 ? bloomIds[i - 1] : renderGraph.GetRTHandle<CameraTarget>();
			pass.ReadTexture("Input", rt);
			pass.AddRenderPassData<ViewData>();

			var width = Mathf.Max(1, camera.pixelWidth >> (i + 1));
			var height = Mathf.Max(1, camera.pixelHeight >> (i + 1));

			pass.SetRenderFunction((new Float2(1.0f / width, 1.0f / height), rt, settings), static (command, pass, data) =>
			{
				pass.SetVector("RcpResolution", data.Item1);
				pass.SetVector("InputScaleLimit", pass.GetScaleLimit2D(data.rt));
				pass.SetFloat("DirtStrength", data.settings.DirtStrength);

				if (data.settings.LensDirt != null)
					pass.SetTexture("LensDirt", data.settings.LensDirt);

				// Lens flare 
				pass.SetFloat("DistortionQuality", data.settings.DistortionQuality);
				pass.SetFloat("Distortion", data.settings.Distortion);
				pass.SetFloat("GhostStrength", data.settings.GhostStrength);
				pass.SetFloat("GhostCount", data.settings.GhostCount);
				pass.SetFloat("GhostSpacing", data.settings.GhostSpacing);
				pass.SetFloat("HaloStrength", data.settings.HaloStrength);
				pass.SetFloat("HaloWidth", data.settings.HaloWidth);
				pass.SetFloat("HaloRadius", data.settings.HaloRadius);
				pass.SetFloat("StreakStrength", data.settings.StreakStrength);

				if (data.settings.StarburstTexture != null)
					pass.SetTexture("StarBurst", data.settings.StarburstTexture);
			});
		}

		// Upsample
		for (var i = mipCount - 1; i > 0; i--)
		{
			var input = bloomIds[i];

			using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Bloom Up");
			pass.Initialize(material, 2);
			pass.WriteTexture(bloomIds[i - 1]);
			pass.ReadTexture("Input", input);

			var width = Mathf.Max(1, camera.pixelWidth >> i);
			var height = Mathf.Max(1, camera.pixelHeight >> i);

			pass.SetRenderFunction((settings, new Float2(1f / width, 1f / height), input), static (command, pass, data) =>
			{
				pass.SetFloat("Strength", data.settings.BloomStrength);
				pass.SetVector("RcpResolution", data.Item2);
				pass.SetVector("InputScaleLimit", pass.GetScaleLimit2D(data.input));

				pass.SetFloat("DirtStrength", data.settings.DirtStrength);

				if (data.settings.LensDirt != null)
					pass.SetTexture("LensDirt", data.settings.LensDirt);

				// Lens flare 
				pass.SetFloat("DistortionQuality", data.settings.DistortionQuality);
				pass.SetFloat("Distortion", data.settings.Distortion);
				pass.SetFloat("GhostStrength", data.settings.GhostStrength);
				pass.SetFloat("GhostCount", data.settings.GhostCount);
				pass.SetFloat("GhostSpacing", data.settings.GhostSpacing);
				pass.SetFloat("HaloStrength", data.settings.HaloStrength);
				pass.SetFloat("HaloWidth", data.settings.HaloWidth);
				pass.SetFloat("HaloRadius", data.settings.HaloRadius);
				pass.SetFloat("StreakStrength", data.settings.StreakStrength);

				if (data.settings.StarburstTexture != null)
					pass.SetTexture("StarBurst", data.settings.StarburstTexture);
			});
		}

		renderGraph.SetRTHandle<CameraBloom>(bloomIds[0]);
		ListPool<ResourceHandle<RenderTexture>>.Release(bloomIds);

		renderGraph.AddProfileEndPass("Bloom");
	}
}
