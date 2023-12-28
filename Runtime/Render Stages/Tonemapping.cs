using System;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace Arycama.CustomRenderPipeline
{
    public class Tonemapping
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

        public Tonemapping(Settings settings, Bloom.Settings bloomSettings, LensSettings lensSettings)
        {
            this.settings = settings;
            this.bloomSettings = bloomSettings;
            this.lensSettings = lensSettings;
            tonemappingMaterial = new Material(Shader.Find("Hidden/Tonemapping")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Render(CommandBuffer command, RenderTargetIdentifier input, RenderTargetIdentifier bloom, bool isSceneView, int width, int height)
        {
            using var profilerScope = command.BeginScopedSample("Tonemapping");

            command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            command.SetGlobalTexture("_MainTex", input);
            command.SetGlobalTexture("_Bloom", bloom);
            command.SetGlobalFloat("_BloomStrength", bloomSettings.Strength);
            command.SetGlobalFloat("_IsSceneView", isSceneView ? 1f : 0f);

            command.SetGlobalFloat("ToeStrength", settings.ToeStrength);
            command.SetGlobalFloat("ToeLength", settings.ToeLength);
            command.SetGlobalFloat("ShoulderStrength", settings.ShoulderStrength);
            command.SetGlobalFloat("ShoulderLength", settings.ShoulderLength);
            command.SetGlobalFloat("ShoulderAngle", settings.ShoulderAngle);
            command.SetGlobalFloat("NoiseIntensity", settings.NoiseIntensity);
            command.SetGlobalFloat("NoiseResponse", settings.NoiseResponse);

            var filmGrainTexture = settings.FilmGrainTexture;
            command.SetGlobalTexture("_GrainTexture", filmGrainTexture);

            var offsetX = Random.value;
            var offsetY = Random.value;
            float uvScaleX = filmGrainTexture ? width / (float)filmGrainTexture.width : 1.0f;
            float uvScaleY = filmGrainTexture ? height / (float)filmGrainTexture.height : 1.0f;
            float scaledOffsetX = offsetX * uvScaleX;
            float scaledOffsetY = offsetY * uvScaleY;

            command.SetGlobalVector("_GrainTextureParams", new Vector4(uvScaleX, uvScaleY, offsetX, offsetY));
            command.SetGlobalFloat("ShutterSpeed", lensSettings.ShutterSpeed);
            command.SetGlobalFloat("Aperture", lensSettings.Aperture);

            command.DrawProcedural(Matrix4x4.identity, tonemappingMaterial, 0, MeshTopology.Triangles, 3);
        }
    }
}