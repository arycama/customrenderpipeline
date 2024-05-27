Shader "Terrain"
{
    Properties
    {
        _Displacement("Displacement", Range(0, 32)) = 1
        _EdgeLength("Tessellation Edge Length", Range(4, 128)) = 64
        _DistanceFalloff("Tessellation Distance Falloff", Float) = 1
        _FrustumThreshold("Frustum Cull Threshold", Float) = 0
        _BackfaceCullThreshold("Backface Cull Threshold", Float) = 0
        _DisplacementMipBias("Displacement Mip Bias", Range(-2, 2)) = 0.5
    }

    SubShader
    {
        Pass
        {
            Name "Terrain"
            Tags { "LightMode" = "Terrain"}

            Stencil
            {
                // Bit 3 = terrain, 1 = non background
                Ref 9
                Pass Replace
                WriteMask 9
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex
            #pragma hull Hull
            #pragma domain Domain
            #pragma fragment Fragment

            #pragma multi_compile _ REFLECTION_PROBE_RENDERING

            #include "Terrain.hlsl"
            ENDHLSL
        }

        Pass
        {
            ColorMask 0
            ZClip [_ZClip]
            Cull Off

            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster"}

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex
            #pragma hull Hull
            #pragma domain Domain
            #pragma fragment Fragment

            #include "Terrain.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "RayTracing"
            Tags { "LightMode" = "RayTracing" }

            HLSLPROGRAM
            #pragma raytracing RayTracing
            #include "TerrainRaytracing.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "RayTracing"
            Tags{ "LightMode" = "RayTracingAmbientOcclusion" }

            HLSLPROGRAM
            #pragma raytracing RayTracing
            #include "TerrainRaytracingAmbientOcclusion.hlsl"
            ENDHLSL
        }
    }
}
