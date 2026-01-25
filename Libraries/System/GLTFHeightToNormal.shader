Shader "Hidden/Export/HeightToNormal"
{
	Properties
	{
		_BumpMap ("Normal Texture", 2D) = "white" {}
		_HeightMap ("Height Texture", 2D) = "white" {}
		_HeightScale("Height Map Scale", float) = 1.0			
		_HeightToNormalStrength("Height to Normal Strength", float) = 0.0		
		_FlipY("Flip texture Y", Int) = 0
		_FlipNormalY("Flip Normal Y (invert green)", Int) = 0
	}
	SubShader
	{
		// No culling or depth
		ZTest Always Cull Off ZWrite Off lighting off
		Fog { Mode off }      
		Pass {
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			#include "GLTFConvertColors.cginc"

			struct vertInput {
			 float4 pos : POSITION;
			 float2 texcoord : TEXCOORD0;
			};

			struct vertOutput {
			 float4 pos : SV_POSITION;
			 float2 texcoord : TEXCOORD0;
			};
			
			sampler2D _BumpMap;
			sampler2D _HeightMap;
			float _HeightScale;
			float _HeightToNormalStrength;
			int _FlipY;
			int _FlipNormalY;
			float4 _HeightMap_TexelSize; // provided automatically by Unity for textures

			#pragma target 3.0

			vertOutput vert(vertInput input) {
				vertOutput o;
				o.pos = UnityObjectToClipPos(input.pos);
				o.texcoord.x = input.texcoord.x;
				if(_FlipY == 1) o.texcoord.y = (1.0 - input.texcoord.y);
				else o.texcoord.y = input.texcoord.y;
				return o;
			}

			float4 frag(vertOutput output) : COLOR {
				float2 uv = output.texcoord;
				float4 bumpmap = tex2D(_BumpMap, uv);
				float3 bumpVec = UnpackNormal(bumpmap); // -1..1 tangent-space normal

				// Sample center + neighbors from height map and linearize if necessary
				float3 hCcol = tex2D(_HeightMap, uv).rgb;
				#if UNITY_COLORSPACE_GAMMA
					hCcol = GammaToLinearSpace(hCcol);
				#endif
				float hC = (hCcol.r + hCcol.g + hCcol.b) * (1.0/3.0);

				float2 texel = _HeightMap_TexelSize.xy;
				float3 hLcol = tex2D(_HeightMap, uv + float2(-texel.x, 0)).rgb;
				float3 hRcol = tex2D(_HeightMap, uv + float2(texel.x, 0)).rgb;
				float3 hDcol = tex2D(_HeightMap, uv + float2(0, -texel.y)).rgb;
				float3 hUcol = tex2D(_HeightMap, uv + float2(0, texel.y)).rgb;
				#if UNITY_COLORSPACE_GAMMA
					hLcol = GammaToLinearSpace(hLcol);
					hRcol = GammaToLinearSpace(hRcol);
					hDcol = GammaToLinearSpace(hDcol);
					hUcol = GammaToLinearSpace(hUcol);
				#endif
				float hL = dot(hLcol, float3(0.3333333,0.3333333,0.3333333));
				float hR = dot(hRcol, float3(0.3333333,0.3333333,0.3333333));
				float hD = dot(hDcol, float3(0.3333333,0.3333333,0.3333333));
				float hU = dot(hUcol, float3(0.3333333,0.3333333,0.3333333));

				// Compute derivatives in UV space (avoid 0 division)
				float dx = 0.0f;
				float dy = 0.0f;
				if (texel.x > 0.0) dx = (hR - hL) * 0.5 / texel.x;
				if (texel.y > 0.0) dy = (hU - hD) * 0.5 / texel.y;

			float hScale = min(1.0, _HeightScale);
			float3 heightNormal = normalize(float3(-dx * hScale, -dy * hScale, 1.0));

			// Combine bump normal and height-derived slope in tangent space if requested
			float blendStrength = saturate(_HeightToNormalStrength);
			float3 combined = bumpVec;
			if (blendStrength > 0.0)
			{
				// apply slope directly to XY components to avoid normalization flattening
				float2 slope = float2(dx, dy) * hScale * blendStrength;
				combined.xy = combined.xy + float2(-slope.x, -slope.y);
				combined = normalize(combined);
			}

			// if (_FlipNormalY == 1) combined.y = -combined.y; // invert green channel (Y)
			// combined.y = -combined.y; // invert green channel (Y)
			float3 outNormal = 0.5f + 0.5f * combined; // encode to 0..1
			float heightOut = saturate(hC * hScale);
				return float4(outNormal, heightOut);
			}
			ENDCG
		}
	}
}
