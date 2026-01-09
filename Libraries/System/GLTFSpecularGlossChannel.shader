Shader "Hidden/Export/SpecularGlossChannel" {
	Properties{
		_SpecularGlossMap("Texture", 2D) = "white" {}
		_AlbedoTexture("Texture", 2D) = "white" {}
		_SmoothnessFromAlbedoAlpha("Smoothness From Albedo Alpha", Int) = 0
		_GlossinessScale("Glossiness Scale", float) = 1.0
		_SpecularColor("Specular Color", Color) = (1,1,1,1)
		_FlipY("Flip texture Y", Int) = 0
        _GLTF("Is GLTF", Int) = 0
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

			sampler2D _SpecularGlossMap;
			sampler2D _AlbedoTexture;
			int _SmoothnessFromAlbedoAlpha;
			float _GlossinessScale;
			float4 _SpecularColor;
			int _FlipY;
			int _GLTF;

			vertOutput vert(vertInput input) {
				vertOutput o;
				o.pos = UnityObjectToClipPos(input.pos);
				o.texcoord.x = input.texcoord.x;
				o.texcoord.y = (_FlipY == 1) ? (1.0 - input.texcoord.y) : input.texcoord.y;
				return o;
			}

			float4 frag(vertOutput output) : COLOR {
				float4 src = tex2D(_SpecularGlossMap, output.texcoord);
				float4 outTex = float4(0.0, 0.0, 0.0, 1.0);

				// Bake specular factor into RGB so glTF specularFactor can be [1,1,1].
				outTex.rgb = src.rgb * _SpecularColor.rgb;

				float glossiness;
				if (_GLTF == 1) {
					glossiness = src.a;
				} else {
					// IMPORTANT: use albedo alpha only when Unity is configured to store smoothness there.
					if (_SmoothnessFromAlbedoAlpha == 1) {
						float4 albedo = tex2D(_AlbedoTexture, output.texcoord);
						glossiness = albedo.a;
					} else {
						glossiness = src.a;
					}
				}

				// Bake glossiness scale into A so glTF glossinessFactor can be 1.0.
				outTex.a = saturate(glossiness * _GlossinessScale);
				return outTex;
			}

			ENDCG
		}
	}
}
