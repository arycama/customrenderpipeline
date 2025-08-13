Shader"Hidden/Blit ColorMask"
{
    Properties
    {
		_Color("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
		[Toggle] _Invert("Invert On", Float) = 0
		[Enum(ColorWriteMask)] _ColorMask("Color Mask", Float) = 15
		_ColorSelector("Color Selector", Vector) = (1, 0, 0, 0)
    }

    SubShader
    {
		ColorMask [_ColorMask]
		Cull Off
		ZTest Always
		ZWrite Off
		
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

			#pragma shader_feature _INVERT_ON

			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float4 _ColorSelector;
			half4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
				float4 color = tex2D(_MainTex, i.uv) * _Color;

				#ifdef _INVERT_ON
					color = 1 - color;
				#endif

                return dot(color, _ColorSelector);
            }

            ENDHLSL
        }
    }
}
