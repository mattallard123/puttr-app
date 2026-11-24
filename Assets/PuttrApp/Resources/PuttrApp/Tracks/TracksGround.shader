// The ground of a Tracks course. Two maps cover the whole hole: the lie's own colour (stripes, roads and all) and how
// much of each kind of ground is there. Up close, scanned grass, rough, scrub and sand give the grain: each scan is
// stored with a mean of 0.4, so colour = lie colour x scan / 0.4 keeps the course's palette. Water mirrors the sky.
// Lit by the sun (with its shadows) and the sky, and hazed with distance.
Shader "PuttrApp/TracksGround"
{
    Properties
    {
        _Macro ("Lie colours", 2D) = "white" {}
        _Control ("Lie weights (r mown, g sand, b scrub, a water)", 2D) = "black" {}
        _Mown ("Mown grass", 2D) = "gray" {}
        _MownN ("Mown grass normal", 2D) = "bump" {}
        _Rough ("Rough", 2D) = "gray" {}
        _RoughN ("Rough normal", 2D) = "bump" {}
        _Scrub ("Scrub", 2D) = "gray" {}
        _ScrubN ("Scrub normal", 2D) = "bump" {}
        _Sand ("Sand", 2D) = "gray" {}
        _SandN ("Sand normal", 2D) = "bump" {}
        _Unit ("World units in a yard", Float) = 1
        _Edge ("How sharply one ground meets the next", Float) = 5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "TracksCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Macro_ST, _Control_ST, _Mown_ST, _MownN_ST, _Rough_ST, _RoughN_ST, _Scrub_ST, _ScrubN_ST, _Sand_ST, _SandN_ST;
            float _Unit, _Edge;
            CBUFFER_END

            TEXTURE2D(_Macro); SAMPLER(sampler_Macro);
            TEXTURE2D(_Control); SAMPLER(sampler_Control);
            TEXTURE2D(_Mown); SAMPLER(sampler_Mown);
            TEXTURE2D(_MownN);
            TEXTURE2D(_Rough);
            TEXTURE2D(_RoughN);
            TEXTURE2D(_Scrub);
            TEXTURE2D(_ScrubN);
            TEXTURE2D(_Sand);
            TEXTURE2D(_SandN);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            // Every scan shares the mown grass's sampler (they all repeat, with the same filtering).
            #define SCAN(tex, uv) SAMPLE_TEXTURE2D(tex, sampler_Mown, uv)

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.positionWS.xz / _Unit;   // yards
                half4 ctl = SAMPLE_TEXTURE2D(_Control, sampler_Control, i.uv);
                half3 macro = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, i.uv).rgb;

                half3 dRough = SCAN(_Rough, p / 1.7).rgb;
                half grain = dot(dRough, half3(0.4167, 0.4167, 0.4167));   // 0.5 on average

                // How much of each ground: the map is soft (a texel is two thirds of a yard), so the edge is drawn
                // again here, sharp and ragged with the grain, the way turf meets sand.
                half fine = saturate(ctl.r * 2.0 - 1.0);   // greens and tees: 1; fairway: 0
                half4 w = half4(saturate(ctl.r * 2.0), ctl.g, ctl.b, ctl.a);
                w = saturate((w - 0.5) * _Edge + 0.5 + (grain - 0.5) * 0.18 * _Edge);
                half wRough = saturate(1.0 - w.x - w.y - w.z - w.w);
                half sum = w.x + w.y + w.z + w.w + wRough;
                w /= sum; wRough /= sum;

                half3 dMown = SCAN(_Mown, p / 0.9).rgb;
                half3 dScrub = SCAN(_Scrub, p / 2.3).rgb;
                half3 dSand = lerp(half3(0.4, 0.4, 0.4), SCAN(_Sand, p / 2.1).rgb, 0.45);   // raked sand: the scan is rougher ground
                half3 detail = (dMown * w.x + dSand * w.y + dScrub * w.z + dRough * wRough) * 2.5;   // 1 on average
                // A second, much wider look at the rough's grain, so the repeat does not show in the middle distance.
                half wide = dot(SCAN(_Rough, p / 41.0).rgb, half3(0.8333, 0.8333, 0.8333));
                detail *= lerp(1.0, wide, 0.4 * (1.0 - w.y));
                half3 albedo = macro * detail;

                half3 nM = UnpackNormal(SCAN(_MownN, p / 0.9));
                half3 nR = UnpackNormal(SCAN(_RoughN, p / 1.7));
                half3 nB = UnpackNormal(SCAN(_ScrubN, p / 2.3));
                half3 nS = UnpackNormal(SCAN(_SandN, p / 2.1));
                half2 bump = nM.xy * w.x * lerp(0.8, 0.4, fine) + nS.xy * w.y * 0.35 + nB.xy * w.z * 1.3 + nR.xy * wRough * 1.2;
                half3 ground = normalize(i.normalWS);
                half3 N = normalize(ground + half3(bump.x, 0.0, bump.y));

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light sun = GetMainLight(shadowCoord, i.positionWS, half4(1, 1, 1, 1));
                half3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                half lit = saturate(dot(N, sun.direction)) * sun.shadowAttenuation;
                half3 col = albedo * (sun.color * lit + TracksAmbient(N));
                // Grass seen along its blades catches the light: a soft sheen at a low angle, none on sand.
                half sheen = pow(1.0 - saturate(dot(ground, V)), 4.0) * (1.0 - w.y - w.w);
                col += albedo * sun.color * sheen * 0.3 * (0.35 + 0.65 * sun.shadowAttenuation);

                if (w.w > 0.003)
                {
                    // Water: two sets of ripples crossing, the sky mirrored more the lower the eye, the sun's glint.
                    float t = _Time.y;
                    half3 r1 = UnpackNormal(SCAN(_RoughN, p / 11.0 + t * float2(0.011, 0.007)));
                    half3 r2 = UnpackNormal(SCAN(_RoughN, p / 4.7 - t * float2(0.008, 0.013)));
                    half3 Nw = normalize(half3((r1.x + r2.x) * 0.07, 1.0, (r1.y + r2.y) * 0.07));
                    half3 R = reflect(-V, Nw);
                    R.y = abs(R.y);
                    half fresnel = 0.03 + 0.97 * pow(1.0 - saturate(dot(Nw, V)), 5.0);
                    half3 deep = macro * 0.35 * (sun.color * (0.3 + 0.7 * sun.shadowAttenuation) + TracksAmbient(Nw));
                    half3 water = lerp(deep, TracksSky(R), fresnel);
                    water += sun.color * pow(saturate(dot(R, sun.direction)), 600.0) * 6.0 * sun.shadowAttenuation;
                    col = lerp(col, water, w.w);
                }

                return half4(TracksHazed(col, i.positionWS, _Unit), 1.0);
            }
            ENDHLSL
        }
    }
}
