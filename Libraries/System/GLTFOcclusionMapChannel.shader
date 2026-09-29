Shader "Hidden/Export/OcclusionMapChannel" {
	Properties{
		_OcclusionMap("Occlusion Map", 2D) = "black" {}
		_FlipY("Flip texture Y", Int) = 0
		_SourceChannel("Source channel: 0 red, 1 green", Int) = 0
		_OcclusionScale("Occlusion remap scale", float) = 1.0
		_OcclusionBias("Occlusion remap bias", float) = 0.0
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

			 sampler2D _OcclusionMap;
			 int _FlipY;
			 int _SourceChannel;
			 float _OcclusionScale;
			 float _OcclusionBias;

			 vertOutput vert(vertInput input) {
				 vertOutput o;
				 o.pos = UnityObjectToClipPos(input.pos);
				 o.texcoord.x = input.texcoord.x;
				 if(_FlipY == 1) o.texcoord.y = (1.0 - input.texcoord.y);
				 else o.texcoord.y = input.texcoord.y;
				 return o;
			 }

			 float4 frag(vertOutput output) : COLOR {
				// plan D15. URP samples _OcclusionMap.g through LerpWhiteTo (LitInput.hlsl:161-169) while
				// glTF and Babylon read R, so a URP packed map has its GREEN channel written into red here.
				// _SourceChannel == 0 reproduces the historical red pass-through for every Built-in and
				// terrain caller, except that the output is now explicitly grey - which the runtime reads as R.
				float4 src = tex2D(_OcclusionMap, output.texcoord);
				float occ = (_SourceChannel == 1) ? src.g : src.r;
				// HDRP remaps the mask's occlusion channel by lerp(_AORemapMin, _AORemapMax, g)
				// (LitDataIndividualLayer.hlsl:260-261), which is `g * scale + bias`. Every other caller
				// passes scale 1 / bias 0 and is unchanged.
				occ = saturate(occ * _OcclusionScale + _OcclusionBias);
				return float4(occ, occ, occ, 1.0);
			 }

			ENDCG
		}
	}
}
