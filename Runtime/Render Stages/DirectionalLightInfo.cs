using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct DirectionalLightInfo : IRenderPassData
    {
        private readonly Vector3 lightDirection0, lightColor0, lightDirection1, lightColor1;
        public int LightCount { get; }

        public DirectionalLightInfo(Vector3 lightDirection0, Vector3 lightColor0, Vector3 lightDirection1, Vector3 lightColor1, int lightCount)
        {
            this.lightDirection0 = lightDirection0;
            this.lightColor0 = lightColor0;
            this.lightDirection1 = lightDirection1;
            this.lightColor1 = lightColor1;
            LightCount = lightCount;
        }

        void IRenderPassData.SetInputs(RenderPass pass)
        {
        }

        void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector(command, "_LightDirection0", lightDirection0);
            pass.SetVector(command, "_LightColor0", lightColor0);
            pass.SetVector(command, "_LightDirection1", lightDirection1);
            pass.SetVector(command, "_LightColor1", lightColor1);
            pass.SetInt(command, "_LightCount", LightCount);
        }
    }
}