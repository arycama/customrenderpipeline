Shader "Hidden/Distance Field"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
		_Offset("Offset", Float) = 0
		_Cutoff("Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
			Name "Generate Seed Pixels"

            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"
			
            sampler2D _MainTex;
			float _Cutoff;
			float4 _MainTex_TexelSize;

			float4 frag(v2f_img i) : SV_Target
			{
				float4 color = tex2D(_MainTex, i.uv);

				float2 offsets[8] = { float2(-1, -1), float2(0, -1), float2(1, -1), float2(-1, 0), float2(1, 0), float2(-1, 1), float2(0, 1), float2(1, 1) };

				if(color.a < _Cutoff)
				{
					for(uint j = 0; j < 8; j++)
					{
						float4 neighbor = tex2Dlod(_MainTex, float4(i.uv + _MainTex_TexelSize.xy * offsets[j], 0, 0));
						if(neighbor.a >= _Cutoff)
						{
							return float4(i.uv, 1, 1);
						}
					}
				}

				return 0;
            }
            ENDHLSL
        }

		Pass
        {
			Name "Jump Flood"

            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"
			
            sampler2D _MainTex;
			float4 _MainTex_TexelSize;
			float _Offset;

			float4 frag(v2f_img i) : SV_Target
			{ 
				float2 offsets[9] = { float2(-1, -1), float2(0, -1), float2(1, -1), float2(-1, 0), float2(0, 0), float2(1, 0), float2(-1, 1), float2(0, 1), float2(1, 1) };
				float minDist = sqrt(2);
				float4 minSeed = 0;

				for(uint j = 0; j < 9; j++)
				{
					float2 uv = i.uv + offsets[j] * _MainTex_TexelSize.xy * _Offset;
					float4 seed = tex2D(_MainTex, uv);

					if (all(seed.zw > 0.5))
					{
						float dist = distance(seed.xy, i.uv);
						if (dist < minDist)
						{
							minDist = dist;
							minSeed = seed;
						}
					}
				}

				return minSeed;
            }
            ENDHLSL
        }

		Pass
        {
			Name "Combine"

            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"
			
            sampler2D _MainTex, _SourceTex;
			float4 _MainTex_TexelSize;
			float _Cutoff;

			float4 frag(v2f_img i) : SV_Target
			{
				float2 offsets[9] = { float2(-1, -1), float2(0, -1), float2(1, -1), float2(-1, 0), float2(0, 0), float2(1, 0), float2(-1, 1), float2(0, 1), float2(1, 1) };
				float minDist = sqrt(2);
				float4 minSeed = 0;

				for(uint j = 0; j < 9; j++)
				{
					float2 uv = i.uv + offsets[j] * _MainTex_TexelSize.xy;
					float4 seed = tex2D(_MainTex, uv);

					if (all(seed.zw > 0.5))
					{
						float dist = distance(seed.xy, i.uv);
						if (dist < minDist)
						{
							minDist = dist;
							minSeed = seed;
						}
					}
				}

				// Sample original color again, invert distance if above threhsold
				float4 color = tex2D(_SourceTex, i.uv);
				if(color.a < _Cutoff)
				{
					minDist *= -1;
				}

				float signedDistance = minDist * 0.5 + 0.5;
				return float4(color.rgb, signedDistance);
            }

            ENDHLSL
        }
    }
}