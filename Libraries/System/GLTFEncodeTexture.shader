Shader "Hidden/Export/EncodeTexture" {
    Properties {
        _MainTex ("Base (HDR RT)", 2D) = "black" {}
		_GammaOut ("Gamma Out", float) = 1.0
        _EncodeHDR ("Encode HDR", int) = 0
        _FlipY("Flip texture Y", Int) = 0
    }
    Subshader  {
        ZTest Always Cull Off ZWrite Off lighting off
        Fog { Mode off }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct vertInput { float4 pos : POSITION; float2 texcoord : TEXCOORD0; };
            struct vertOutput { float4 pos : SV_POSITION; float2 texcoord : TEXCOORD0; };

            sampler2D _MainTex;
			float _GammaOut;
            int _EncodeHDR;
            int _FlipY;

            static const float rgbdMaxRange = 255.0;
            static const float GammaEncodePowerApprox  = 1.0 / 2.2;

            float3 toGammaSpace(float3 c) { return pow(c, GammaEncodePowerApprox.xxx); }

            float4 EncodeRGBD(float3 colorLinear)
            {
                colorLinear = max(colorLinear, 0.0);

                float maxRGB = max(colorLinear.r, max(colorLinear.g, colorLinear.b));
                if (maxRGB <= 0.0) return float4(0,0,0,1);

                float D = max(rgbdMaxRange / maxRGB, 1.0);
                D = clamp(floor(D) / 255.0, 0.0, 1.0);

                float3 rgb = toGammaSpace(colorLinear * D);
                return float4(rgb, D);
            }

            vertOutput vert(vertInput v) {
                vertOutput o;
                o.pos = UnityObjectToClipPos(v.pos);
                o.texcoord = v.texcoord;
                if (_FlipY == 1) o.texcoord.y = 1.0 - o.texcoord.y;
                return o;
            }

            float4 frag(vertOutput i) : SV_Target {
                float4 result = tex2D(_MainTex, i.texcoord);
                if (_EncodeHDR == 1) result = EncodeRGBD(result.rgb);
                return result;
            }
            ENDCG
        }
    }
    Fallback off
}
