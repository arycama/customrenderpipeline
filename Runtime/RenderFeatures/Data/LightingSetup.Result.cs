using UnityEngine;
using UnityEngine.Rendering;

public partial class LightingSetup
{
	public readonly struct Result : IRenderPassData
	{
		private readonly ResourceHandle<GraphicsBuffer> pointLights;
		private readonly int pointLightCount;

		public Result(ResourceHandle<GraphicsBuffer> pointLights, int pointLightCount)
		{
			this.pointLights = pointLights;
			this.pointLightCount = pointLightCount;
		}

		public void SetInputs(RenderPassBase pass)
		{
			pass.ReadBuffer("PointLights", pointLights);
		}

		public void SetProperties(RenderPassBase pass, CommandBuffer command)
		{
			pass.SetInt("PointLightCount", pointLightCount);
		}
	}
}
