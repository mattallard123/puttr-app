// The lit shader of an old Apple chip (Arcade.Plain: an A9 or A10), in place of Universal Render Pipeline/Lit.
//
// On an A9X iPad Pro the pipeline's own Lit shader drew the mat and every floor pure white, with every light, the
// reflections, the textures, the shadows and the normal maps off (2026-11-20). The Glow shader beside this one drew
// correctly on the same iPad, so this is built the same way: one pass, nothing from the pipeline's lighting.
//
// It is one sun and an even light from everywhere, both set for the frame by PlainLights (_PlainSunDir,
// _PlainSunColor, _PlainAmbient): texture x colour x light, plus the material's glow. No shadows, no shine, no
// reflections. The property names are Lit's, so a material keeps what it was given when its shader is changed, and
// a see-through one keeps its blending (_SrcBlend, _DstBlend, _ZWrite). Everything is worked out in full floats.
// It lives in Resources so builds include it.
Shader "PuttrArcade/PlainLit"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [HDR] _EmissionColor ("Glow", Color) = (0, 0, 0, 1)
        _Smoothness ("Smoothness (unused)", Float) = 0.5
        _Metallic ("Metallic (unused)", Float) = 0
        _BumpMap ("Normal map (unused)", 2D) = "bump" {}
        _BumpScale ("Normal scale (unused)", Float) = 1
        _SrcBlend ("Source blend", Float) = 1
        _DstBlend ("Destination blend", Float) = 0
        _ZWrite ("Depth write", Float) = 1
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _EmissionColor;
                float _Smoothness;
                float _Metallic;
                float _BumpScale;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _Cull;
            CBUFFER_END

            float4 _PlainSunDir;     // towards the sun, world space
            float4 _PlainSunColor;
            float4 _PlainAmbient;

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float4 paint = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                // A normal of no length (a mesh without normals) faces up: never divide by nothing.
                float len = length(i.normalWS);
                float3 n = len > 0.0001 ? i.normalWS / len : float3(0.0, 1.0, 0.0);
                float facing = saturate(dot(n, _PlainSunDir.xyz));
                float3 light = _PlainAmbient.rgb + _PlainSunColor.rgb * facing;
                float3 c = paint.rgb * light + _EmissionColor.rgb;
                return float4(saturate(c), saturate(paint.a));
            }
            ENDHLSL
        }
    }
}
