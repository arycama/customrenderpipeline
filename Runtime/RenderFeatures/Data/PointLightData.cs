using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    public readonly struct PointLightData : IRenderPassData
    {
        public readonly ResourceHandle<GraphicsBuffer> dataBuffer, lightBuffer, lightDepthMinMaxBuffer, pointLightIndices, spotLightIndices;
        public readonly ResourceHandle<RenderTexture> visibleLightBits;
        public readonly ResourceHandle<RenderTexture>? pointShadows;
        public readonly int pointLightCount, spotLightCount, intersectingPointLightCount, intersectingSpotLightCount;

        public PointLightData(ResourceHandle<GraphicsBuffer> dataBuffer, ResourceHandle<GraphicsBuffer> lightBuffer, int pointLightCount, int spotLightCount, ResourceHandle<GraphicsBuffer> lightDepthMinMaxBuffer, ResourceHandle<RenderTexture> visibleLightBits, int intersectingPointLightCount, int intersectingSpotLightCount, ResourceHandle<GraphicsBuffer> pointLightIndices, ResourceHandle<GraphicsBuffer> spotLightIndices, ResourceHandle<RenderTexture>? pointShadows)
        {
            this.dataBuffer = dataBuffer;
            this.lightBuffer = lightBuffer;
            this.pointLightCount = pointLightCount;
            this.spotLightCount = spotLightCount;
            this.lightDepthMinMaxBuffer = lightDepthMinMaxBuffer;
            this.visibleLightBits = visibleLightBits;
            this.intersectingPointLightCount = intersectingPointLightCount;
            this.intersectingSpotLightCount = intersectingSpotLightCount;
            this.pointLightIndices = pointLightIndices;
            this.spotLightIndices = spotLightIndices;
            this.pointShadows = pointShadows;
        }

        void IRenderPassData.SetInputs(RenderPass pass)
        {
            pass.ReadBuffer("PointLightData", dataBuffer);
            pass.ReadBuffer("PointLights", lightBuffer);
            pass.ReadBuffer("PointLightIndices", pointLightIndices);
            pass.ReadBuffer("SpotLightIndices", spotLightIndices);
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