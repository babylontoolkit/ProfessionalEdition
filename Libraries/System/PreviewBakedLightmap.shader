Shader "Hidden/Debug/PreviewBakedLightmap" {
    Properties {
        _MainTex ("Baked Lightmap (EXR)", 2D) = "black" {}
        _Exposure ("Exposure", Float) = 1.0
    }
    SubShader {
        Tags { "RenderType" = "Opaque" }
        Pass {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
            };

            sampler2D _MainTex;
            float _Exposure;

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 DecodeLightmap(float4 data) {
                // Unity-style decode assuming RGBM/RGBD off
                return data.rgb;
            }

            fixed4 frag (v2f i) : SV_Target {
                float3 color = DecodeLightmap(tex2D(_MainTex, i.uv));
                color *= _Exposure;
                return float4(color, 1.0);
            }
            ENDCG
        }
    }
}
