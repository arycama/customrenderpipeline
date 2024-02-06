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

        [Header("Soft Shadows")]
        [SerializeField, Range(1, 32)] private int pcfSamples = 4;
        [SerializeField, Min(0f)] private float pcfRadius = 1f;
        [SerializeField, Range(1, 32)] private int blockerSamples = 4;
        [SerializeField, Min(0f)] private float blockerRadius = 1f;
        [SerializeField, Min(0f)] private float pcssSoftness = 1f;

        public int ShadowCascades => shadowCascades;
        public Vector3 ShadowCascadeSplits => shadowCascadeSplits;
        public float ShadowDistance => shadowDistance;
        public int DirectionalShadowResolution => directionalShadowResolution;
        public float ShadowBias => shadowBias;
        public float ShadowSlopeBias => shadowSlopeBias;
        public int PointShadowResolution => pointShadowResolution;
        public float PointShadowBias => pointShadowBias;
        public float PointShadowSlopeBias => pointShadowSlopeBias;
        public int PcfSamples => pcfSamples;
        public float PcfRadius => pcfRadius;
        public int BlockerSamples => blockerSamples;
        public float BlockerRadius => blockerRadius;
        public float PcssSoftness => pcssSoftness;
    }
}