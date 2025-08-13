using UnityEngine;
using UnityEngine.Rendering;

public partial class ScreenSpaceShadows
{
	public struct Result : IRenderPassData
	{
		private ResourceHandle<RenderTexture> screenSpaceShadows;
		private float intensity;

		public Result(ResourceHandle<RenderTexture> screenSpaceShadows, float intensity)
		{
			this.screenSpaceShadows = screenSpaceShadows;
			this.intensity = intensity;
		}

		public void SetInputs(RenderPassBase pass)
		{
			pass.ReadTexture("ScreenSpaceShadows", screenSpaceShadows);
		}

		public void SetProperties(RenderPassBase pass, CommandBuffer command)
		{
			pass.SetFloat("ScreenSpaceShadowsIntensity", intensity);
		}
	}
}