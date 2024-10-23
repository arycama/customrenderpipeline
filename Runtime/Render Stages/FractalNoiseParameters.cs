using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    [Serializable]
    public class FractalNoiseParameters
    {
        [field: SerializeField, Range(1, 16)] public int Frequency { get; private set; } = 4;
        [field: SerializeField, Range(0.0f, 1.0f)] public float H { get; private set; } = 1.0f;
        [field: SerializeField, Range(1, 9)] public int Octaves { get; private set; } = 1;

        public float FractalBound => Maths.Exp2(-(Octaves - 1) * H) * (Maths.Exp2((Octaves - 1) * H + H) - 1.0f) * Maths.Rcp(Maths.Exp2(H) - 1.0f);
    }
}