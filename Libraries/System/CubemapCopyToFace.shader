Shader "Hidden/CubemapCopyToFace"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _Cube;
            int _Face; // 0..5

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 FaceUvToDir(int face, float2 uv)
            {
                // uv in [0..1] -> a in [-1..1]
                float2 a = uv * 2.0 - 1.0;

                // CubemapFace order: +X, -X, +Y, -Y, +Z, -Z
                if (face == 0) return normalize(float3( 1, -a.y, -a.x)); // +X
                if (face == 1) return normalize(float3(-1, -a.y,  a.x)); // -X
                if (face == 2) return normalize(float3( a.x,  1,  a.y)); // +Y
                if (face == 3) return normalize(float3( a.x, -1, -a.y)); // -Y
                if (face == 4) return normalize(float3( a.x, -a.y,  1)); // +Z
                             return normalize(float3(-a.x, -a.y, -1)); // -Z
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 dir = FaceUvToDir(_Face, i.uv);
                return texCUBE(_Cube, dir);
            }
            ENDHLSL
        }
    }
}
