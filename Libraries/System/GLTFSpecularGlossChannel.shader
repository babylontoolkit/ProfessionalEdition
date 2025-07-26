Shader "Hidden/Export/SpecularGlossChannel" {
	Properties{
		_SpecularGlossMap("Texture", 2D) = "white" {}
		_AlbedoTexture("Texture", 2D) = "white" {}
		_AlbedoHasAlpha("Albedo Has Alpha", Int) = 0
		_GlossinessScale("Glossiness Scale", float) = 1.0
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
			int _AlbedoHasAlpha;
			float _GlossinessScale;
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
				float4 unitySpecGloss = tex2D(_SpecularGlossMap, output.texcoord);
				float4 gltfSpecGloss = float4(0.0, 0.0, 0.0, 1.0);

				if (_GLTF == 1) {
					// glTF texture already packed as SpecularGlossiness
					// R = Spec.R, G = Spec.G, B = Spec.B, A = Glossiness
					gltfSpecGloss = unitySpecGloss;
				} else {
					// Unity-style Specular workflow:
					// RGB = specular color
					// A = glossiness (to be passed through)
					gltfSpecGloss.rgb = unitySpecGloss.rgb;

					float glossiness;
					if (_AlbedoHasAlpha == 1) {
						float4 albedo = tex2D(_AlbedoTexture, output.texcoord);
						glossiness = albedo.a;
					} else {
						glossiness = unitySpecGloss.a;
					}

					// Optional: apply glossiness scale if needed
					// gltfSpecGloss.a = glossiness * _GlossinessScale;

					// Final result: unscaled for clean glTF export
					gltfSpecGloss.a = glossiness;
				}

				return gltfSpecGloss;
			}

			ENDCG
		}
	}
}
