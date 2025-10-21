using UnityEngine;
using UnityEngine.Rendering;

public partial class VolumetricLighting
{
	public readonly struct Result : IRenderPassData
    {
        private readonly ResourceHandle<RenderTexture> volumetricLighting;
		private readonly ResourceHandle<GraphicsBuffer> volumetricLightingData;

        public Result(ResourceHandle<RenderTexture> volumetricLighting, ResourceHandle<GraphicsBuffer> volumetricLightingData)
        {
            this.volumetricLighting = volumetricLighting;
            this.volumetricLightingData = volumetricLightingData;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("VolumetricLighting", volumetricLighting);
			pass.ReadBuffer("VolumetricLightingData", volumetricLightingData);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
			pass.SetVector("VolumetricLightScale", pass.GetScale3D(volumetricLighting));
			pass.SetVector("VolumetricLightMax", pass.GetLimit3D(volumetricLighting));
		}
    }
}