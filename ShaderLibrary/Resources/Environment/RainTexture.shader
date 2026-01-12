Shader "Hidden/Rain Texture"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment Fragment
            #include "RainTexture.hlsl"
            ENDHLSL
        }
    }
}