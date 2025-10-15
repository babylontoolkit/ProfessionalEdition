Shader "Hidden/Export/MetalGlossChannel" {
    Properties{
        _MetallicGlossMap("Texture", 2D) = "white" {}
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

            sampler2D _MetallicGlossMap;
            sampler2D _AlbedoTexture;
            int _AlbedoHasAlpha;
            float _GlossinessScale;
            int _FlipY;
            int _GLTF;

            vertOutput vert(vertInput input) {
                vertOutput o;
                o.pos = UnityObjectToClipPos(input.pos);
                o.texcoord.x = input.texcoord.x;
                if (_FlipY == 1)
                    o.texcoord.y = 1.0 - input.texcoord.y;
                else
                    o.texcoord.y = input.texcoord.y;
                return o;
            }

            float4 frag(vertOutput output) : COLOR {
                float4 unityMetalGloss = tex2D(_MetallicGlossMap, output.texcoord);
                float4 gltfMetalRough = float4(0.0, 0.0, 0.0, 1.0);
                if (_GLTF == 1) {
                    // Texture is already in glTF metallic-roughness format
                    gltfMetalRough.b = unityMetalGloss.b; // metallic  (B)
                    gltfMetalRough.g = unityMetalGloss.g; // roughness (G)
                } else {
                    // Convert from Unity's metallic-smoothness map
                    gltfMetalRough.b = unityMetalGloss.r; // Unity R → glTF B
                    // Unity smoothness → glTF roughness
                    float smoothness;
                    if (_AlbedoHasAlpha == 1) {
                        float4 albedo = tex2D(_AlbedoTexture, output.texcoord);
                        smoothness = albedo.a;
                    } else {
                        smoothness = unityMetalGloss.a;
                    }
                    gltfMetalRough.g = 1.0 - (smoothness * _GlossinessScale);
                }
                gltfMetalRough.r = 0.0;   // Unused
                gltfMetalRough.a = 1.0;   // Fully opaque
                return gltfMetalRough;
            }

            ENDCG
        }
    }
}
