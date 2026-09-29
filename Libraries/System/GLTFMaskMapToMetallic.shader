Shader "Hidden/Export/MaskMapToMetalRoughChannel" {
    Properties{
        _MaskMap("Texture", 2D) = "white" {}
        _GlossinessScale("Glossiness Scale", float) = 1.0
        _MetallicScale("Metallic Scale", float) = 1.0
        _MetallicBias("Metallic Bias", float) = 0.0
        _GlossinessBias("Glossiness Bias", float) = 0.0
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

            struct vertInput {
                float4 pos : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct vertOutput {
                float4 pos : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MaskMap;
            float _GlossinessScale;
            float _MetallicScale;
            float _MetallicBias;
            float _GlossinessBias;
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
                // Unity MaskMap: R=Metallic, G=AO, B=DetailMask, A=Smoothness
                float4 m = tex2D(_MaskMap, output.texcoord);

                // HDRP remaps each channel: lerp(remapMin, remapMax, sample), which is
                // `sample * scale + bias` (LitDataIndividualLayer.hlsl:242-243, 258-261). Built-in and
                // URP pass bias 0, so `sample * scale` is unchanged for them.
                float metallic = saturate(m.r * _MetallicScale + _MetallicBias);
                float smoothness = saturate(m.a * _GlossinessScale + _GlossinessBias);
                float roughness = 1.0 - smoothness;

                float4 outTex;
                outTex.r = 0.0;
                outTex.g = saturate(roughness);
                outTex.b = metallic;
                outTex.a = 1.0;
                return outTex;
            }

            ENDCG
        }
    }
}
