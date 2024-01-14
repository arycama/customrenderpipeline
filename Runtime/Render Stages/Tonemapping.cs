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

        public void Render(RTHandle input, RTHandle bloom, bool isSceneView, int width, int height)
        {
            var pass = renderGraph.AddRenderPass<FullscreenRenderPass>(new FullscreenRenderPass(material));

            pass.ReadTexture("_MainTex", input);
            pass.ReadTexture("_Bloom", bloom);

            pass.SetRenderFunction((command, context) =>
            {
                pass.SetTexture(command, "_GrainTexture", settings.FilmGrainTexture);

                pass.SetFloat(command, "_BloomStrength", bloomSettings.Strength);
                pass.SetFloat(command, "_IsSceneView", isSceneView ? 1f : 0f);
                pass.SetFloat(command, "ToeStrength", settings.ToeStrength);
                pass.SetFloat(command, "ToeLength", settings.ToeLength);
                pass.SetFloat(command, "ShoulderStrength", settings.ShoulderStrength);
                pass.SetFloat(command, "ShoulderLength", settings.ShoulderLength);
                pass.SetFloat(command, "ShoulderAngle", settings.ShoulderAngle);
                pass.SetFloat(command, "NoiseIntensity", settings.NoiseIntensity);
                pass.SetFloat(command, "NoiseResponse", settings.NoiseResponse);

                var offsetX = Random.value;
                var offsetY = Random.value;
                var uvScaleX = settings.FilmGrainTexture ? width / (float)settings.FilmGrainTexture.width : 1.0f;
                var uvScaleY = settings.FilmGrainTexture ? height / (float)settings.FilmGrainTexture.height : 1.0f;

                pass.SetVector(command, "_GrainTextureParams", new Vector4(uvScaleX, uvScaleY, offsetX, offsetY));
                pass.SetFloat(command, "ShutterSpeed", lensSettings.ShutterSpeed);
                pass.SetFloat(command, "Aperture", lensSettings.Aperture);

                command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                pass.Execute(command);
            });
        }
    }
}