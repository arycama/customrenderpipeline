Shader "Hidden/Depth of Field"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma use_dxc
			#pragma require waveMath
            #include "DepthOfField.hlsl"
            ENDHLSL
        }
    }
}