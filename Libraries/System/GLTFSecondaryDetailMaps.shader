
Shader "Hidden/Export/SecondaryDetailMaps" {
	Properties{
		_AlbedoTexture("Texture", 2D) = "white" {}
		_NormalTexture("Texture", 2D) = "white" {}
		_RoughnessTexture("Texture", 2D) = "white" {}
		_MaskTexture("Texture", 2D) = "white" {}
		_FlipY("Flip texture Y", Int) = 0
	}

	SubShader {
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

			 sampler2D _AlbedoTexture;
			 sampler2D _NormalTexture;
			 sampler2D _RoughnessTexture;
			sampler2D _MaskTexture;
			 int _FlipY;

			 vertOutput vert(vertInput input) {
				 vertOutput o;
				 o.pos = UnityObjectToClipPos(input.pos);
				 o.texcoord.x = input.texcoord.x;
				 if(_FlipY == 1) o.texcoord.y = (1.0 - input.texcoord.y);
				 else o.texcoord.y = input.texcoord.y;
				 return o;
			 }

			 float4 frag(vertOutput output) : COLOR {
				// Babylon.js detail map packing (PBR):
				// R = detail albedo (grayscale)
				// G = detail normal Y
				// B = detail roughness
				// A = detail normal X
			 	float4 final = float4(0.0, 0.0, 0.0 ,1.0);
				float4 albedo2 = tex2D(_AlbedoTexture, output.texcoord);
			 	float4 bump2 = tex2D(_NormalTexture, output.texcoord);
				fixed3 normal2 = 0.5f + 0.5f * UnpackNormal(bump2);
				float4 roughness2 = tex2D(_RoughnessTexture, output.texcoord);
				float mask = tex2D(_MaskTexture, output.texcoord).r;
				mask = saturate(mask);
				//
				float detailLuma = dot(albedo2.rgb, float3(0.2126, 0.7152, 0.0722));
				detailLuma = lerp(0.5, detailLuma, mask);
				float2 detailNormalXY = lerp(float2(0.5, 0.5), normal2.xy, mask);
				float detailRoughness = lerp(0.5, roughness2.r, mask);
				final.r = detailLuma;
				final.g = detailNormalXY.y;
				final.b	= detailRoughness;
			 	final.a = detailNormalXY.x;
				return final;
			 }

			ENDCG
		}
	}
}
