using System;
using UnityEngine;

[Serializable]
public class FractalNoiseParameters
{
	[field: SerializeField, Range(1, 16)] public int Frequency { get; private set; } = 4;
	[field: SerializeField, Range(0.0f, 1.0f)] public float H { get; private set; } = 1.0f;
	[field: SerializeField, Range(1, 9)] public int Octaves { get; private set; } = 1;

	public float FractalBound => Math.Exp2(-(Octaves - 1) * H) * (Math.Exp2((Octaves - 1) * H + H) - 1.0f) * Math.Rcp(Math.Exp2(H) - 1.0f);
}