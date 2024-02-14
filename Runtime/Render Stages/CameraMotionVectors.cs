using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class CameraMotionVectors : RenderFeature
    {
        private readonly Material material;

        public CameraMotionVectors(RenderGraph renderGraph) : base(renderGraph)
        {
            material = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Render(RTHandle motionVectors, RTHandle cameraDepth, int width, int height, Matrix4x4 nonJitteredVpMatrix, Matrix4x4 previousVpMatrix, Matrix4x4 invVpMatrix)
        {
            using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Camera Motion Vectors");
            pass.Initialize(material);
            pass.ReadTexture("_CameraDepth", cameraDepth);
            pass.WriteTexture(motionVectors);
            pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);

            var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
            {
                pass.SetVector(command, "_ScaledResolution", data.scaledResolution);
                pass.SetMatrix(command, "_WorldToNonJitteredClip", data.nonJitteredVpMatrix);
                pass.SetMatrix(command, "_WorldToPreviousClip", data.previousVpMatrix);
                pass.SetMatrix(command, "_ClipToWorld", data.invVpMatrix);
            });

            data.scaledResolution = new Vector4(width, height, 1.0f / width, 1.0f / height);
            data.nonJitteredVpMatrix = nonJitteredVpMatrix;
            data.previousVpMatrix = previousVpMatrix;
            data.invVpMatrix = invVpMatrix;
        }

        private class PassData
        {
            internal Vector4 scaledResolution;
            internal Matrix4x4 nonJitteredVpMatrix;
            internal Matrix4x4 previousVpMatrix;
            internal Matrix4x4 invVpMatrix;
        }
    }

    public class VolumetricClouds : RenderFeature
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField] public float startHeight { get; private set; } = 1024.0f;
            [field: SerializeField] public float layerThickness { get; private set; } = 512.0f;
            [field: SerializeField] public int raySamples { get; private set; } = 32;
            [field: SerializeField] public int lightSamples { get; private set; } = 5;
            [field: SerializeField] public float lightDistance { get; private set; } = 512.0f;

            [field: SerializeField] public Texture2D weatherTexture { get; private set; } = null;
            [field: SerializeField] public float weatherScale { get; private set; } = 32768.0f;
            [field: SerializeField] public Vector2 windSpeed { get; private set; } = Vector2.zero;

            [field: SerializeField] public Texture3D noiseTexture { get; private set; } = null;
            [field: SerializeField] public float noiseScale { get; private set; } = 4096.0f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float noiseStrength { get; private set; } = 1.0f;

            [field: SerializeField] public Texture3D detailTexture { get; private set; } = null;
            [field: SerializeField] public float detailScale { get; private set; } = 512.0f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float detailStrength { get; private set; } = 1.0f;

        }

        private readonly Material material;

        public VolumetricClouds(RenderGraph renderGraph) : base(renderGraph)
        {
            material = new Material(Shader.Find("Hidden/Volumetric Clouds")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Render(RTHandle cameraTarget, RTHandle cameraDepth, int width, int height, IRenderPassData commonPassData)
        {
            using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds");
            pass.Initialize(material);
            pass.WriteTexture(cameraTarget);
            pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.ReadTexture("_Depth", cameraDepth);

            var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
            {
            });
        }

        private class PassData
        {
        }
    }
}