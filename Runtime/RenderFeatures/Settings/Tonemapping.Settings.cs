using System;
using UnityEngine;

public partial class Tonemapping
{
	[Serializable]
	public class Settings
	{
		[field: Header("Settings")]
		[field: SerializeField] public float PaperWhite = 160.0f;
		[field: SerializeField] public float MinLuminance { get; private set; } = 0;
		[field: SerializeField] public float MaxLuminance { get; private set; } = 1000;
		[field: SerializeField] public bool Hdr { get; private set; } = true;
		[field: SerializeField] public bool Tonemap { get; private set; } = true;
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
