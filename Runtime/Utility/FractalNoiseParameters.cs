using System;
using UnityEngine;
using static Unmath.Math;

[Serializable]
public class FractalNoiseParameters
{
	[field: SerializeField, Range(1, 16)] public int Frequency { get; private set; } = 4;
	[field: SerializeField, Range(0.0f, 1.0f)] public float H { get; private set; } = 1.0f;
	[field: SerializeField, Range(1, 9)] public int Octaves { get; private set; } = 1;

	public float FractalBound => Exp2(-(Octaves - 1) * H) * (Exp2((Octaves - 1) * H + H) - 1.0f) * Rcp(Exp2(H) - 1.0f);
}
