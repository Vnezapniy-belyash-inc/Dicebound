Shader "Dicebound/FogCover"
{
    Properties { _FogMask("Fog", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_FogMask); SAMPLER(sampler_FogMask);
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input) { Varyings o; o.positionCS = TransformObjectToHClip(input.positionOS.xyz); o.uv = input.uv; return o; }
            half4 Frag(Varyings input) : SV_Target
            {
                half2 state = SAMPLE_TEXTURE2D(_FogMask, sampler_FogMask, input.uv).rg;
                return half4(0,0,0, max(state.r, state.g) > 0.5 ? 0 : 1);
            }
            ENDHLSL
        }
    }
}
