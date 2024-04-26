using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    [Serializable]
    public class ShadowSettings
    {
        [Header("Directional Shadows")]
        [SerializeField, Range(1, 4)] private int shadowCascades = 1;
        [SerializeField] private Vector3 shadowCascadeSplits = new Vector3(0.25f, 0.5f, 0.75f);
        [SerializeField] private float shadowDistance = 4096;
        [SerializeField] private int directionalShadowResolution = 2048;
        [SerializeField] private float shadowBias = 0.0f;
        [SerializeField] private float shadowSlopeBias = 0.0f;

        [Header("Point Shadows")]
        [SerializeField] private int pointShadowResolution = 256;
        [SerializeField] private float pointShadowBias = 0.0f;
        [SerializeField] private float pointShadowSlopeBias = 0.0f;

        [field: Header("Soft Shadows")]
        [field: SerializeField, Range(0, 8)] public float PcfFilterRadius { get; private set; } = 1f;
        [field: SerializeField, Min(0f)] public float PcfFilterSigma { get; private set; } = 1f;

        public int ShadowCascades => shadowCascades;
        public Vector3 ShadowCascadeSplits => shadowCascadeSplits;
        public float ShadowDistance => shadowDistance;
        public int DirectionalShadowResolution => directionalShadowResolution;
        public float ShadowBias => shadowBias;
        public float ShadowSlopeBias => shadowSlopeBias;
        public int PointShadowResolution => pointShadowResolution;
        public float PointShadowBias => pointShadowBias;
        public float PointShadowSlopeBias => pointShadowSlopeBias;
    }
}