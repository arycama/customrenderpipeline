Shader "Hidden/Ambient Occlusion"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        
        Pass
        {
            Stencil
            {
                Ref 0
                Comp NotEqual
            }
        
            Blend DstColor Zero
            
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }
    }
}