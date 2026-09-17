Shader "Hidden/Export/EncodeLightmap"
{
    Properties
    {
        _MainTex ("Base (HDR RT)", 2D) = "black" {}

        // LDR preview gamma (ONLY used when _Rgbd == 0)
        _Correct ("Color Correction (gamma 2.2)", Int) = 1

        _FlipY ("Flip Texture Y", Int) = 0

        // 1 = encode RGBD for Babylon; 0 = plain LDR output
        _Rgbd ("Encode RGBD", Int) = 0

        // plan D23: SRP selects the lightmap decode at COMPILE time from UNITY_LIGHTMAP_*_ENCODING and has
        // no unity_Lightmap_HDR uniform, so a manual Graphics.Blit can never learn the encoding. The
        // exporter passes it in instead. exponent 0 => full-HDR/dLDR (alpha unused); exponent > 0 => RGBM.
        _DecodeMult ("Lightmap decode multiplier", Float) = 1.0
        _DecodeExp  ("Lightmap decode exponent",   Float) = 0.0
    }

    SubShader
    {
        ZTest Always
        Cull Off
        ZWrite Off
        Lighting Off
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM

            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0

            #include "UnityCG.cginc"
            #include "GLTFConvertColors.cginc"   // for DecodeLightmap, toGammaSpace/toLinearSpace if you use them

            struct vertInput
            {
                float4 pos      : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct vertOutput
            {
                float4 pos      : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            int       _Correct;
            int       _FlipY;
            int       _Rgbd;
            float     _DecodeMult;
            float     _DecodeExp;

            // ------------------------------------------------------------------
            // Babylon-compatible RGBD encode (matches helperFunctions.fx: toRGBD)
            // colorIn is LINEAR HDR (DecodeLightmap output).
            // ------------------------------------------------------------------

            static const float rgbdMaxRange = 255.0;
            static const float LinearEncodePowerApprox = 2.2;
            static const float GammaEncodePowerApprox  = 1.0 / LinearEncodePowerApprox;

            float3 toGammaSpace(float3 c) { return pow(c, GammaEncodePowerApprox.xxx); }

            float4 EncodeRGBD(float3 colorLinear)
            {
                // Color is linear HDR illumination
                float maxRGB = max(colorLinear.r, max(colorLinear.g, colorLinear.b));

                if (maxRGB <= 0.0)
                {
                    // Black texel – keep divisor sane
                    return float4(0, 0, 0, 1.0);
                }

                // Same logic as Babylon's toRGBD
                float D = max(rgbdMaxRange / maxRGB, 1.0);
                D = clamp(floor(D) / 255.0, 0.0, 1.0);

                // Apply divisor and gamma-encode RGB for 8-bit storage
                float3 rgb = colorLinear * D;
                rgb = toGammaSpace(rgb);   // matches Babylon: "rgb = toGammaSpace(rgb);"

                return float4(rgb, D);
            }

            // ------------------------------------------------------------------

            vertOutput vert (vertInput v)
            {
                vertOutput o;
                o.pos = UnityObjectToClipPos(v.pos);

                o.texcoord.x = v.texcoord.x;
                o.texcoord.y = (_FlipY == 1) ? (1.0 - v.texcoord.y) : v.texcoord.y;

                return o;
            }

            float4 frag (vertOutput i) : SV_Target
            {
                // 1. Sample Unity’s baked lightmap RT (encoded)
                float4 src = tex2D(_MainTex, i.texcoord);

                // 2. Decode to linear HDR illumination (Unity’s lighting data)
                // plan D23: the decode SRP picks at compile time, passed in as uniforms. Full HDR is
                // (mult 1, exp 0) - a pass-through - so a High-encoding project exports byte-identical
                // output to the historical DecodeLightmap(src) call this replaced.
                float3 lightLinear = (_DecodeExp > 0.0)
                    ? src.rgb * (pow(max(src.a, 1e-5), _DecodeExp) * _DecodeMult)
                    : src.rgb * _DecodeMult;

                // 3. RGBD export path for Babylon
                if (_Rgbd == 1)
                {
                    // IMPORTANT:
                    // - No extra gamma before EncodeRGBD.
                    // - Babylon will decode automatically if you set
                    //   texture.isRGBD = true and texture.gammaSpace = false.
                    return EncodeRGBD(lightLinear);
                }

                // 4. Non-RGBD path (debug / preview): optional gamma correction
                float3 outColor = lightLinear;

                if (_Correct == 1)
                {
                    // Simple gamma 2.2 so the 8-bit texture looks “right” in viewers.
                    outColor = pow(outColor, 1.0 / 2.2);
                }

                return float4(outColor, 1.0);
            }

            ENDCG
        }
    }

    Fallback Off
}
