using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    public readonly struct PointLightData : IRenderPassData
    {
        public readonly ResourceHandle<GraphicsBuffer> dataBuffer, lightBuffer, lightDepthMinMaxBuffer;
        public readonly ResourceHandle<RenderTexture> visibleLightBits;
        public readonly ResourceHandle<RenderTexture>? pointShadows;
        public readonly int lightCount, intersectingLightCount;

        public PointLightData(ResourceHandle<GraphicsBuffer> dataBuffer, ResourceHandle<GraphicsBuffer> lightBuffer, int lightCount, ResourceHandle<GraphicsBuffer> lightDepthMinMaxBuffer, ResourceHandle<RenderTexture> visibleLightBits, int intersectingLightCount, ResourceHandle<RenderTexture>? pointShadows)
        {
            this.dataBuffer = dataBuffer;
            this.lightBuffer = lightBuffer;
            this.lightCount = lightCount;
            this.lightDepthMinMaxBuffer = lightDepthMinMaxBuffer;
            this.visibleLightBits = visibleLightBits;
            this.intersectingLightCount = intersectingLightCount;
            this.pointShadows = pointShadows;
        }

        void IRenderPassData.SetInputs(RenderPass pass)
        {
            pass.ReadBuffer("PointLightData", dataBuffer);
            pass.ReadBuffer("PointLights", lightBuffer);
            pass.ReadBuffer("LightDepthMinMax", lightDepthMinMaxBuffer);
            pass.ReadTexture("VisibleLightBits", visibleLightBits);

            if(pointShadows.HasValue)
                pass.ReadTexture("PointShadows", pointShadows.Value);
        }

        void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}