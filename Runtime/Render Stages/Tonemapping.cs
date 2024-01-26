using System;
using UnityEditor;
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
        private readonly Material material;

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
            material = new Material(Shader.Find("Hidden/Tonemapping")) { hideFlags = HideFlags.HideAndDontSave };
        }

        class PassData
        {
            public Texture2D grainTexture;
            public float bloomStrength, isSceneView, toeStrength, toeLength, shoulderStrength, shoulderLength, shoulderAngle, noiseIntensity, noiseResponse, shutterSpeed, aperture;
            public Vector4 grainTextureParams;
        }

        public void Render(RTHandle input, RTHandle bloom, bool isSceneView, int width, int height)
        {
            using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>();
            pass.Material = material;
            pass.Index = 0;

            pass.ReadTexture("_MainTex", input);
            pass.ReadTexture("_Bloom", bloom);
            pass.WriteScreen();

            var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
            {
                pass.SetTexture(command, "_GrainTexture", data.grainTexture);

                pass.SetFloat(command, "_BloomStrength", data.bloomStrength);
                pass.SetFloat(command, "_IsSceneView", data.isSceneView);
                pass.SetFloat(command, "ToeStrength", data.toeStrength);
                pass.SetFloat(command, "ToeLength", data.toeLength);
                pass.SetFloat(command, "ShoulderStrength", data.shoulderStrength);
                pass.SetFloat(command, "ShoulderLength", data.shoulderLength);
                pass.SetFloat(command, "ShoulderAngle", data.shoulderAngle);
                pass.SetFloat(command, "NoiseIntensity", data.noiseIntensity);
                pass.SetFloat(command, "NoiseResponse", data.noiseResponse);

                pass.SetFloat(command, "ShutterSpeed", data.shutterSpeed);
                pass.SetFloat(command, "Aperture", data.aperture);
                pass.SetVector(command, "_GrainTextureParams", data.grainTextureParams);
            });

            var offsetX = Random.value;
            var offsetY = Random.value;
            var uvScaleX = settings.FilmGrainTexture ? width / (float)settings.FilmGrainTexture.width : 1.0f;
            var uvScaleY = settings.FilmGrainTexture ? height / (float)settings.FilmGrainTexture.height : 1.0f;

            data.grainTexture = settings.FilmGrainTexture;
            data.bloomStrength = bloomSettings.Strength;
            data.isSceneView = isSceneView ? 1.0f : 0.0f;
            data.toeStrength = settings.ToeStrength;
            data.toeLength = settings.ToeLength;
            data.shoulderStrength = settings.ShoulderStrength;
            data.shoulderLength = settings.ShoulderLength;
            data.shoulderAngle = settings.ShoulderAngle;
            data.noiseIntensity = settings.NoiseIntensity;
            data.noiseResponse = settings.NoiseResponse;
            data.shutterSpeed = lensSettings.ShutterSpeed;
            data.aperture = lensSettings.Aperture;
            data.grainTextureParams = new Vector4(uvScaleX, uvScaleY, offsetX, offsetY);
        }
    }
}