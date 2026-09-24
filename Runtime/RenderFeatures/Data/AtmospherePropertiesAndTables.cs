using UnityEngine;
using UnityEngine.Rendering;
using Unmath;

namespace CustomRenderPipeline
{
    public struct AtmospherePropertiesAndTables : IRenderPassData
    {
        private readonly ResourceHandle<GraphicsBuffer> atmospherePropertiesBuffer;
        private readonly ResourceHandle<RenderTexture> transmittance;
        private readonly ResourceHandle<RenderTexture> multiScatter;
        private readonly ResourceHandle<RenderTexture> groundAmbient;
        private readonly ResourceHandle<RenderTexture> skyAmbient;

        public AtmospherePropertiesAndTables(ResourceHandle<GraphicsBuffer> atmospherePropertiesBuffer, ResourceHandle<RenderTexture> transmittance, ResourceHandle<RenderTexture> multiScatter, ResourceHandle<RenderTexture> groundAmbient, ResourceHandle<RenderTexture> skyAmbient)
        {
            this.atmospherePropertiesBuffer = atmospherePropertiesBuffer;
            this.transmittance = transmittance;
            this.multiScatter = multiScatter;
            this.groundAmbient = groundAmbient;
            this.skyAmbient = skyAmbient;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadBuffer("AtmosphereProperties", atmospherePropertiesBuffer);
            pass.ReadTexture("SkyTransmittance", transmittance);
            pass.ReadTexture("_MultiScatter", multiScatter);
            pass.ReadTexture("_SkyAmbient", skyAmbient);
            pass.ReadTexture("_GroundAmbient", groundAmbient);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}