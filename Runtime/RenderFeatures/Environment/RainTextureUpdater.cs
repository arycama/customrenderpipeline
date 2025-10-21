using System;
using UnityEngine;
using UnityEngine.Rendering;

public class RainTextureUpdater : CameraRenderFeature
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField] public int Resolution { get; private set; } = 256;
		[field: SerializeField] public float Size { get; private set; } = 1;
	}

	private readonly Settings settings;
	private readonly Material material;

	public RainTextureUpdater(RenderGraph renderGraph, Settings settings) : base(renderGraph)
	{
		this.settings = settings;
		material = new Material(Shader.Find("Hidden/Rain Texture")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var rainTexture = renderGraph.GetTexture(settings.Resolution, settings.Resolution, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8_SNorm, isExactSize: true, hasMips: true, autoGenerateMips: true);

		using var pass = renderGraph.AddFullscreenRenderPass("Rain Texture", (settings.Resolution, settings.Size));
		pass.Initialize(material);

		pass.WriteTexture(rainTexture);

		pass.AddRenderPassData<FrameData>();
		pass.AddRenderPassData<ViewData>();

		pass.SetRenderFunction(static (command, pass, data) =>
		{
			pass.SetFloat("Resolution", data.Resolution);
			pass.SetFloat("Size", data.Size);
		});

		renderGraph.SetResource(new RainTextureResult(rainTexture, settings.Size));
	}
}
