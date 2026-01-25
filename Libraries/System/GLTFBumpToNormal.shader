Shader "Hidden/Export/BumpToNormal"
{
	Properties
	{
		_BumpMap ("Normal Texture", 2D) = "white" {}
		_FlipY("Flip texture Y", Int) = 0			
		_FlipNormalY("Flip Normal Y (invert green)", Int) = 0
	}
	SubShader
	{
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
			int _FlipY;
			int _FlipNormalY;

			 vertOutput vert(vertInput input) {
				 vertOutput o;
				 o.pos = UnityObjectToClipPos(input.pos);
				 o.texcoord.x = input.texcoord.x;
				 if(_FlipY == 1) o.texcoord.y = (1.0 - input.texcoord.y);
				 else o.texcoord.y = input.texcoord.y;
				 return o;
			 }

			float4 frag(vertOutput output) : COLOR {
				float4 bump = tex2D(_BumpMap, output.texcoord);
				float3 unpack = UnpackNormal(bump);
				// if (_FlipNormalY == 1) unpack.y = -unpack.y; // invert green channel (Y)
				// unpack.y = -unpack.y; // invert green channel (Y)
 				fixed3 normal = (0.5f + 0.5f * unpack);
				return float4(normal.rgb, 1.0);
			}
			ENDCG
		}
	}
}
