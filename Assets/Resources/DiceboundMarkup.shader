Shader "Dicebound/Markup"
{
    Properties { _Color("Color", Color) = (1,1,1,1) _FogMask("Fog", 2D) = "black" {} _FogEnabled("Fog enabled", Float) = 1 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_FogMask); SAMPLER(sampler_FogMask);
            float4 _Color; float _FogEnabled; float4x4 _GridWorldToLocal; float4 _GridSize;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; };
            Varyings Vert(Attributes input) { Varyings o; o.world = TransformObjectToWorld(input.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.world); return o; }
            half4 Frag(Varyings input) : SV_Target
            {
                if (_FogEnabled > 0.5)
                {
                    float2 uv = mul(_GridWorldToLocal, float4(input.world,1)).xz / max(_GridSize.xy, 0.001) + 0.5;
                    if (any(uv < 0) || any(uv > 1)) discard;
                    // Show only the currently visible fragments; door state outside vision stays concealed.
                    half sight = SAMPLE_TEXTURE2D(_FogMask, sampler_FogMask, uv).r;
                    clip(sight - 0.5);
                }
                return _Color;
            }
            ENDHLSL
        }
    }
}
