Shader "Dicebound/FogMap"
{
    Properties
    {
        [MainTexture] _BaseMap("Map", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _FogMask("Fog", 2D) = "black" {}
        _FogEnabled("Enabled", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_FogMask); SAMPLER(sampler_FogMask);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _FogEnabled;
                float4 _GridSize;
                float4x4 _GridWorldToLocal;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 fogUV : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                float3 local = mul(_GridWorldToLocal, float4(world, 1)).xyz;
                output.fogUV = local.xz / _GridSize.xy + 0.5;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                if (_FogEnabled < 0.5) return color;
                half2 mask = SAMPLE_TEXTURE2D(_FogMask, sampler_FogMask, input.fogUV).rg;
                if (any(input.fogUV < 0) || any(input.fogUV > 1)) mask = 0;
                if (mask.r > 0.5) return color;
                if (mask.g > 0.5)
                {
                    half gray = dot(color.rgb, half3(0.2126, 0.7152, 0.0722));
                    return half4(gray.xxx * 0.45, color.a);
                }
                return half4(0,0,0,1);
            }
            ENDHLSL
        }
    }
}
