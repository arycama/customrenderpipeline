Shader "Hidden/Bloom"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

        Pass
        {
            Name "Downsample First"
        
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentDownsample
            #define FIRST
            #define FLIP
            #include "Bloom.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Downsample"
        
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentDownsample
            #define FLIP
            #include "Bloom.hlsl"
            ENDHLSL
        }
        
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
        
            Name "Upsample"
            
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentUpsample
            #define FLIP
            #include "Bloom.hlsl"
            ENDHLSL
        }
    }
}