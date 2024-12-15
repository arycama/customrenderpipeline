using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public partial class Tonemapping
    {
        [Serializable]
        public class Settings
        {
            [field: Header("Tonemapping")]
            [field: SerializeField] public bool Tonemap { get; private set; } = true;

            [field: Header("Tonescale Parameters")]
            [field: SerializeField, Range(80, 480)] public float PaperWhite { get; private set; } = 203;
            [field: SerializeField, Range(0, 0.5f)] public float GreyLuminanceBoost { get; private set; } = 0.12f;
            [field: SerializeField, Range(1.0f, 2.0f)] public float Contrast { get; private set; } = 1.4f;
            [field: SerializeField, Range(0, 0.02f)] public float Toe { get; private set; } = 0.001f;

            [field: Header("Color Parameters")]
            [field: SerializeField, Range(0, 1)] public float PurityCompress { get; private set; } = 0.3f;
            [field: SerializeField, Range(0, 1)] public float PurityBoost { get; private set; } = 0.3f;
            [field: SerializeField, Range(-1, 1)] public float HueshiftR { get; private set; } = 0.3f;
            [field: SerializeField, Range(-1, 1)] public float HueshiftG { get; private set; } = 0;
            [field: SerializeField, Range(-1, 1)] public float HueshiftB { get; private set; } = -0.3f;

            [field: Header("Hdr Output")]
            [field: SerializeField] public bool HdrEnabled { get; private set; } = true;
            [field: SerializeField] public HDRDisplayBitDepth BitDepth { get; private set; } = HDRDisplayBitDepth.BitDepth10;

            // TODO: Move to lens settings or something?
            [field: Header("Film Grain")]
            [field: SerializeField, Range(0.0f, 1.0f)] public float NoiseIntensity { get; private set; } = 0.5f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float NoiseResponse { get; private set; } = 0.8f;
            [field: SerializeField] public Texture2D FilmGrainTexture { get; private set; } = null;
        }
    }
}
