using System;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace Arycama.CustomRenderPipeline
{
    public class Tonemapping : RenderFeature
    {
        private readonly Settings settings;
        private readonly Bloom.Settings bloomSettings;
        private readonly LensSettings lensSettings;
        private readonly Material tonemappingMaterial;

        [Serializable]
        public class Settings
        {
            [SerializeField, Range(0.0f, 1.0f)] private float noiseIntensity = 0.5f;
            [SerializeField, Range(0.0f, 1.0f)] private float noiseResponse = 0.8f;
            [SerializeField] private Texture2D filmGrainTexture = null;
            [SerializeField] private float toeStrength = 0.5f;
            [SerializeField] private float toeLength = 0.5f;
            [SerializeField] private float shoulderStrength = 2.0f;
            [SerializeField] private float shoulderLength = 0.5f;
            [SerializeField] private float shoulderAngle = 1.0f;

            public float NoiseIntensity => noiseIntensity;
            public float NoiseResponse => noiseResponse;
            public Texture2D FilmGrainTexture => filmGrainTexture;
            public float ToeStrength => toeStrength;
            public float ToeLength => toeLength;
            public float ShoulderStrength => shoulderStrength;
            public float ShoulderLength => shoulderLength;
            public float ShoulderAngle => shoulderAngle;
        }

        public Tonemapping(Settings settings, Bloom.Settings bloomSettings, LensSettings lensSettings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            this.bloomSettings = bloomSettings;
            this.lensSettings = lensSettings;
            tonemappingMaterial = new Material(Shader.Find("Hidden/Tonemapping")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Render(RTHandle input, RTHandle bloom, bool isSceneView, int width, int height)
        {
            renderGraph.AddRenderPass((command, context) =>
            {
                using var propertyBlock = renderGraph.GetScopedPropertyBlock();

                propertyBlock.SetTexture("_MainTex", input);
                propertyBlock.SetTexture("_Bloom", bloom);

                propertyBlock.SetFloat("_BloomStrength", bloomSettings.Strength);
                propertyBlock.SetFloat("_IsSceneView", isSceneView ? 1f : 0f);
                propertyBlock.SetFloat("ToeStrength", settings.ToeStrength);
                propertyBlock.SetFloat("ToeLength", settings.ToeLength);
                propertyBlock.SetFloat("ShoulderStrength", settings.ShoulderStrength);
                propertyBlock.SetFloat("ShoulderLength", settings.ShoulderLength);
                propertyBlock.SetFloat("ShoulderAngle", settings.ShoulderAngle);
                propertyBlock.SetFloat("NoiseIntensity", settings.NoiseIntensity);
                propertyBlock.SetFloat("NoiseResponse", settings.NoiseResponse);

                var filmGrainTexture = settings.FilmGrainTexture;
                propertyBlock.SetTexture("_GrainTexture", filmGrainTexture);

                var offsetX = Random.value;
                var offsetY = Random.value;
                var uvScaleX = filmGrainTexture ? width / (float)filmGrainTexture.width : 1.0f;
                var uvScaleY = filmGrainTexture ? height / (float)filmGrainTexture.height : 1.0f;

                propertyBlock.SetVector("_GrainTextureParams", new Vector4(uvScaleX, uvScaleY, offsetX, offsetY));
                propertyBlock.SetFloat("ShutterSpeed", lensSettings.ShutterSpeed);
                propertyBlock.SetFloat("Aperture", lensSettings.Aperture);

                using var profilerScope = command.BeginScopedSample("Tonemapping");
                command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                command.DrawProcedural(Matrix4x4.identity, tonemappingMaterial, 0, MeshTopology.Triangles, 3, 1, propertyBlock);
            });
        }
    }
}