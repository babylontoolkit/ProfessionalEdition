Shader "Hidden/Babylon/CubemapResample"
{
    Properties
    {
        _Env ("Env Cubemap", CUBE) = "" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _Env;
            int _FaceIndex;
            int _Convention; // 0 = DX, 1 = alternate (must match PrefilterGGX)

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // MUST match PrefilterGGX FaceUvToDir mapping.
            float3 FaceDir(int faceIndex, float2 uv01)
            {
                float2 st = uv01 * 2.0 - 1.0; // [-1,1]
                float s = st.x;
                float t = st.y;

                if (_Convention == 0)
                {
                    // DirectX convention (matches your exporter)
                    if (faceIndex == 0) return normalize(float3( 1.0, -t, -s)); // +X
                    if (faceIndex == 1) return normalize(float3(-1.0, -t,  s)); // -X
                    if (faceIndex == 2) return normalize(float3( s,  1.0,  t)); // +Y
                    if (faceIndex == 3) return normalize(float3( s, -1.0, -t)); // -Y
                    if (faceIndex == 4) return normalize(float3( s, -t,  1.0)); // +Z
                                      return normalize(float3(-s, -t, -1.0)); // -Z
                }
                else
                {
                    // Alternate convention (only if you ever set _Convention=1)
                    if (faceIndex == 0) return normalize(float3( 1.0, -t,  s)); // +X
                    if (faceIndex == 1) return normalize(float3(-1.0, -t, -s)); // -X
                    if (faceIndex == 2) return normalize(float3( s,  1.0, -t)); // +Y
                    if (faceIndex == 3) return normalize(float3( s, -1.0,  t)); // -Y
                    if (faceIndex == 4) return normalize(float3(-s, -t,  1.0)); // +Z
                                      return normalize(float3( s, -t, -1.0)); // -Z
                }
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 dir = FaceDir(_FaceIndex, i.uv);
                float3 col = texCUBE(_Env, dir).rgb;
                return float4(col, 1.0);
            }

            ENDHLSL
        }
    }
}
