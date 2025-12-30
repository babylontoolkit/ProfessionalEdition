Shader "Hidden/BabylonIBL/PrefilterGGX"
{
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

            samplerCUBE _EnvCube;
            int _FaceIndex;      // 0..5 in +X,-X,+Y,-Y,+Z,-Z
            int _SampleCount;    // 0 => handled in C#
            float _Roughness;    // 0..1
            int _Convention;     // 0 = DirectX-like (Unity DDS), 1 = alternate

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Hammersley
            float RadicalInverse_VdC(uint bits)
            {
                bits = (bits << 16) | (bits >> 16);
                bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
                bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
                bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
                bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
                return float(bits) * 2.3283064365386963e-10; // / 0x100000000
            }

            float2 Hammersley(uint i, uint N)
            {
                return float2((float)i / (float)N, RadicalInverse_VdC(i));
            }

            float3 ImportanceSampleGGX(float2 Xi, float roughness, float3 N)
            {
                float a = roughness * roughness;

                float phi = 2.0 * UNITY_PI * Xi.x;
                float cosTheta = sqrt((1.0 - Xi.y) / (1.0 + (a*a - 1.0) * Xi.y));
                float sinTheta = sqrt(1.0 - cosTheta * cosTheta);

                float3 H;
                H.x = cos(phi) * sinTheta;
                H.y = sin(phi) * sinTheta;
                H.z = cosTheta;

                // tangent space -> world
                float3 up = abs(N.z) < 0.999 ? float3(0,0,1) : float3(1,0,0);
                float3 tangent = normalize(cross(up, N));
                float3 bitangent = cross(N, tangent);

                float3 sampleVec = tangent * H.x + bitangent * H.y + N * H.z;
                return normalize(sampleVec);
            }

            // Face UV -> direction (DirectX-ish cubemap)
            float3 FaceUvToDir(float2 uv, int faceIndex, int convention)
            {
                float2 st = uv * 2.0 - 1.0; // [-1,1]
                float s = st.x;
                float t = st.y;

                // Most “scrambled skybox” issues are due to these signs.
                // convention=0 matches typical DDS/DirectX cubemap layout in Unity.
                // convention=1 is an alternate sign set you can try for Babylon differences.
                if (convention == 0)
                {
                    if (faceIndex == 0) return normalize(float3( 1.0, -t, -s)); // +X
                    if (faceIndex == 1) return normalize(float3(-1.0, -t,  s)); // -X
                    if (faceIndex == 2) return normalize(float3( s,  1.0,  t)); // +Y
                    if (faceIndex == 3) return normalize(float3( s, -1.0, -t)); // -Y
                    if (faceIndex == 4) return normalize(float3( s, -t,  1.0)); // +Z
                    return                 normalize(float3(-s, -t, -1.0));     // -Z
                }
                else
                {
                    // Alternate: flips some axes
                    if (faceIndex == 0) return normalize(float3( 1.0, -t,  s)); // +X
                    if (faceIndex == 1) return normalize(float3(-1.0, -t, -s)); // -X
                    if (faceIndex == 2) return normalize(float3( s,  1.0, -t)); // +Y
                    if (faceIndex == 3) return normalize(float3( s, -1.0,  t)); // -Y
                    if (faceIndex == 4) return normalize(float3(-s, -t,  1.0)); // +Z
                    return                 normalize(float3( s, -t, -1.0));     // -Z
                }
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 R = FaceUvToDir(i.uv, _FaceIndex, _Convention);
                float roughness = saturate(_Roughness);

                // Roughness 0 => exact lookup
                if (roughness <= 1e-5)
                {
                    float3 c0 = texCUBE(_EnvCube, R).rgb;
                    return float4(c0, 1);
                }

                uint N = (uint)max(_SampleCount, 1);
                float3 Ndir = R;
                float3 V = R;

                float3 prefiltered = 0;
                float totalWeight = 0;

                [loop]
                for (uint s = 0; s < N; s++)
                {
                    float2 Xi = Hammersley(s, N);
                    float3 H = ImportanceSampleGGX(Xi, roughness, Ndir);
                    float3 L = normalize(2.0 * dot(V, H) * H - V);

                    float NoL = saturate(dot(Ndir, L));
                    if (NoL > 0.0)
                    {
                        float3 c = texCUBE(_EnvCube, L).rgb;
                        prefiltered += c * NoL;
                        totalWeight += NoL;
                    }
                }

                prefiltered /= max(totalWeight, 1e-4);
                return float4(prefiltered, 1);
            }
            ENDHLSL
        }
    }
}
