using System;
using System.Collections.Generic;
using UnityEngine;
using static Unmath.Math;

[Serializable]
public class FractalNoiseParameters : IEquatable<FractalNoiseParameters>
{
	[field: SerializeField, Range(1, 16)] public int Frequency { get; private set; } = 4;
	[field: SerializeField, Range(0.0f, 1.0f)] public float H { get; private set; } = 1.0f;
	[field: SerializeField, Range(1, 9)] public int Octaves { get; private set; } = 1;

	public float FractalBound => Exp2(-(Octaves - 1) * H) * (Exp2((Octaves - 1) * H + H) - 1.0f) * Rcp(Exp2(H) - 1.0f);

    public override bool Equals(object obj) => Equals(obj as FractalNoiseParameters);
    public bool Equals(FractalNoiseParameters other) => other is not null && Frequency == other.Frequency && H == other.H && Octaves == other.Octaves && FractalBound == other.FractalBound;
    public override int GetHashCode() => HashCode.Combine(Frequency, H, Octaves, FractalBound);

    public static bool operator ==(FractalNoiseParameters left, FractalNoiseParameters right) => EqualityComparer<FractalNoiseParameters>.Default.Equals(left, right);
    public static bool operator !=(FractalNoiseParameters left, FractalNoiseParameters right) => !(left == right);
}
