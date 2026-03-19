using System;
using UnityEngine;

public partial class Tonemapping
{
	[Serializable]
	public class Settings
	{
		[field: Header("Settings")]
        [field: SerializeField] public bool Tonemap { get; private set; } = true;
        [field: SerializeField, Range(0, 1)] public float ShoulderCompression { get; private set; } = 0.75f;
        [field: SerializeField, Range(0, 1)] public float LinearStart { get; private set; } = 0.538f;
        [field: SerializeField, Range(0, 1)] public float ShoulderStart { get; private set; } = 0.444f;
        [field: SerializeField, Min(1)] public float ToeStrength { get; private set; } = 1.28f;
        [field: SerializeField, Min(0)] public float FadeStart { get; private set; } = 0.98f;
        [field: SerializeField, Min(0)] public float FadeEnd { get; private set; } = 1.16f;
        [field: SerializeField, Range(0, 1)] public float BlendRatio { get; private set; } = 0.6f;

        [field: SerializeField] public int LutResolution { get; private set; } = 32;
        [field: SerializeField] public bool UseLut { get; private set; } = true;

        [field: SerializeField] public bool Hdr { get; private set; } = true;
        [field: SerializeField] public float PaperWhite { get; private set; } = 160.0f;
        [field: SerializeField] public float MinLuminance { get; private set; } = 0;
		[field: SerializeField] public float MaxLuminance { get; private set; } = 1000;

		[field: SerializeField] public HDRDisplayBitDepth BitDepth { get; private set; } = HDRDisplayBitDepth.BitDepth10;

		[field: Header("Purkinje")]
		[field: SerializeField] public bool Purkinje { get; private set; } = false;
		[field: SerializeField] public bool NormalizeLmsr { get; private set; } = true;
		[field: SerializeField, ColorUsage(false, true)] public Color RodColor { get; private set; } = new Color(0.63721f, 0.39242f, 1.6064f);

#if false
		[Header("Exposure Fusion")]
		[SerializeField] private bool enableLtm = true;
		[SerializeField] private bool boostLocalContrast = false;
		[SerializeField] private float exposure = 1.0f;
		[SerializeField] private float shadows = 1.5f;
		[SerializeField] private float highlights = 2.0f;
		[SerializeField, Range(0, 12)] private int mip = 4;
		[SerializeField, Range(0, 6)] private int displayMip = 2;
		[SerializeField, Range(0.0f, 20.0f)] private float exposurePreferenceSigma = 5.0f;
#endif
	}
}
