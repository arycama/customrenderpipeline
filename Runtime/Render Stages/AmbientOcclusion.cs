using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class AmbientOcclusion : RenderFeature
    {
        private readonly Settings settings;
        private readonly Material material;

        private PersistentRTHandleCache temporalCache;

        public AmbientOcclusion(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Ambient Occlusion")) { hideFlags = HideFlags.HideAndDontSave };
            temporalCache = new(GraphicsFormat.R8G8B8A8_UNorm, renderGraph, "Physical Sky");
        }

        public void Render(Camera camera, RTHandle depth, float scale, Texture2D blueNoise2D, Matrix4x4 invVpMatrix, RTHandle normal, ICommonPassData commonPassData, RTHandle velocity, ref RTHandle bentNormalOcclusion)
        {
            var scaledWidth = (int)(camera.pixelWidth * scale);
            var scaledHeight = (int)(camera.pixelHeight * scale);

            var tempResult = renderGraph.GetTexture(scaledWidth, scaledHeight, GraphicsFormat.R8G8B8A8_UNorm);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ambient Occlusion/Compute"))
            {
                pass.Initialize(material, 0, 1, null, camera);
                pass.WriteTexture(tempResult);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_Normals", normal);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<Pass1Data>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);

                    pass.SetVector(command, "ScaleOffset", data.scaleOffset);
                    pass.SetVector(command, "_UvToView", data.uvToView);
                    pass.SetFloat(command, "_Radius", data.radius);
                    pass.SetFloat(command, "_AoStrength", data.aoStrength);
                    pass.SetFloat(command, "_FalloffScale", data.falloffScale);
                    pass.SetFloat(command, "_FalloffBias", data.falloffBias);
                    pass.SetInt(command, "_DirectionCount", data.directionCount);
                    pass.SetInt(command, "_SampleCount", data.sampleCount);
                    pass.SetTexture(command, "_BlueNoise2D", data.blueNoise2d);
                    pass.SetVector(command, "_ScaledResolution", data.scaledResolution);
                    pass.SetMatrix(command, "_ClipToWorld", data.invVpMatrix);
                    pass.SetVector(command, "_CameraDepthScaleLimit", depth.ScaleLimit2D);
                });

                var tanHalfFovY = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                var tanHalfFovX = tanHalfFovY * camera.aspect;

                data.scaleOffset = new Vector2(1.0f / scaledWidth, 1.0f / scaledHeight);
                data.uvToView = new Vector4(tanHalfFovX * 2.0f, tanHalfFovY * 2.0f, -tanHalfFovX, -tanHalfFovY);
                data.radius = settings.Radius * scaledHeight / tanHalfFovY * 0.5f;
                data.aoStrength = settings.Strength;
                data.falloffScale = settings.Falloff == 1.0f ? 0.0f : 1.0f / (settings.Radius * settings.Falloff - settings.Radius);
                data.falloffBias = settings.Falloff == 1.0f ? 1.0f : 1.0f / (1.0f - settings.Falloff);
                data.directionCount = settings.DirectionCount;
                data.sampleCount = settings.SampleCount;
                data.blueNoise2d = blueNoise2D;
                data.scaledResolution = new Vector4(scaledWidth, scaledHeight, 1.0f / scaledWidth, 1.0f / scaledHeight);
                data.invVpMatrix = invVpMatrix;
            }

            var (current, history, wasCreated) = temporalCache.GetTextures(scaledWidth, scaledHeight, camera, true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ambient Occlusion Temporal"))
            {
                pass.Initialize(material, 1, camera: camera);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", tempResult);
                pass.ReadTexture("_History", history);
                pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("Velocity", velocity);

                commonPassData.SetInputs(pass);
                pass.AddRenderPassData<TemporalAA.TemporalAAData>();

                var data = pass.SetRenderFunction<TemporalPassData>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);
                    pass.SetFloat(command, "_IsFirst", wasCreated ? 1.0f : 0.0f);
                    pass.SetVector(command, "_HistoryScaleLimit", history.ScaleLimit2D);
                });
            }

            var newBentNormalOcclusion = renderGraph.GetTexture(scaledWidth, scaledHeight, GraphicsFormat.R8G8B8A8_UNorm);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ambient Occlusion Resolve"))
            {
                pass.Initialize(material, 2);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(newBentNormalOcclusion, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", current);
                pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);
                pass.AddRenderPassData<TemporalAA.TemporalAAData>();

                var data = pass.SetRenderFunction<TemporalPassData>((command, pass, data) =>
                {
                    pass.SetFloat(command, "_AoStrength", settings.Strength);
                    pass.SetVector(command, "InputScaleLimit", current.ScaleLimit2D);
                });
            }

            bentNormalOcclusion = newBentNormalOcclusion;
        }

        [Serializable]
        public class Settings
        {
            [SerializeField, Range(0.0f, 8.0f)] private float strength = 1.0f;
            [SerializeField] private Color tint = Color.black;
            [SerializeField] private float radius = 5.0f;
            [SerializeField, Range(0f, 1f)] private float falloff = 0.75f;
            [SerializeField, Range(1, 8)] private int directionCount = 1;
            [SerializeField, Range(1, 32)] private int sampleCount = 8;

            public float Strength => strength;
            public Color Tint => tint;
            public float Radius => radius;
            public float Falloff => falloff;
            public int DirectionCount => directionCount;
            public int SampleCount => sampleCount;
        }

        private class Pass0Data
        {
            internal Vector2 scaleOffset;
            internal Vector4 scaledResolution;
            internal Matrix4x4 invVpMatrix;
        }

        private class Pass1Data
        {
            internal Vector2 scaleOffset;
            internal Vector4 uvToView;
            internal float radius;
            internal float aoStrength;
            internal float falloffScale;
            internal float falloffBias;
            internal int directionCount;
            internal int sampleCount;
            internal Texture2D blueNoise2d;
            internal Vector4 scaledResolution;
            internal Matrix4x4 invVpMatrix;
        }

        private class Pass2Data
        {
            internal VolumetricLighting.Result volumetricLightingResult;
            internal Vector4 scaledResolution;
        }

        private class TemporalPassData
        {
        }
    }
}