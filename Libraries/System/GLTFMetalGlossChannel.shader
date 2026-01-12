Shader "Hidden/Export/MetalGlossChannel" {
    Properties{
        _MetallicGlossMap("Texture", 2D) = "white" {}
        _AlbedoTexture("Texture", 2D) = "white" {}
        _SmoothnessFromAlbedoAlpha("Smoothness From Albedo Alpha", Int) = 0
        _GlossinessScale("Glossiness Scale", float) = 1.0
        _MetallicScale("Metallic Scale", float) = 1.0
        _FlipY("Flip texture Y", Int) = 0
        _GLTF("Is GLTF", Int) = 0 // Note: No Longer Used
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

            sampler2D _MetallicGlossMap;
            sampler2D _AlbedoTexture;
            int _SmoothnessFromAlbedoAlpha;
            float _GlossinessScale;
            float _MetallicScale;
            int _FlipY;
            int _GLTF; // Note: No Longer Used

            vertOutput vert(vertInput input) {
                vertOutput o;
                o.pos = UnityObjectToClipPos(input.pos);
                o.texcoord.x = input.texcoord.x;
                o.texcoord.y = (_FlipY == 1) ? (1.0 - input.texcoord.y) : input.texcoord.y;
                return o;
            }

            float4 frag(vertOutput output) : COLOR {
                float4 src = tex2D(_MetallicGlossMap, output.texcoord);
                float4 outTex = float4(0.0, 0.0, 0.0, 1.0);
                float metallic = src.r;
                float smoothness;
                if (_SmoothnessFromAlbedoAlpha == 1) {
                    float4 albedo = tex2D(_AlbedoTexture, output.texcoord);
                    smoothness = albedo.a;
                } else {
                    smoothness = src.a;
                }
                float roughness = 1.0 - (smoothness * _GlossinessScale);
                outTex.b = saturate(metallic);
                outTex.g = saturate(roughness);
                outTex.a = 1.0;
                return outTex;
            }

            ENDCG
        }
    }
}



