// Polished steel on a Tracks course (the tee markers): it mirrors the photographed sky above the horizon and the turf
// below it, catches the sun as a highlight, takes shadows and hazes with distance like the ground.
Shader "PuttrApp/TracksSteel"
{
    Properties
    {
        _Tint ("Steel", Color) = (0.80, 0.82, 0.85, 1)
        _Turf ("What it mirrors below the horizon", Color) = (0.12, 0.22, 0.07, 1)
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
            float4 _Tint, _Turf;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 N = normalize(i.normalWS);
                half3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS), i.positionWS, half4(1, 1, 1, 1));
                half3 R = reflect(-V, N);
                // The sky where the mirror looks up, sunlit turf where it looks down.
                half3 turf = _Turf.rgb * (sun.color * (0.3 + 0.7 * sun.shadowAttenuation) + TracksAmbient(half3(0, 1, 0)));
                half3 mirror = lerp(turf, TracksSky(half3(R.x, max(R.y, 0.0), R.z)), smoothstep(-0.08, 0.08, R.y));
                half fresnel = pow(1.0 - saturate(dot(N, V)), 5.0);
                half3 col = mirror * lerp(_Tint.rgb * 0.9, half3(1, 1, 1), fresnel);
                // A little of the plate's own grey, so a face mirroring dark trees is still metal, and the sun's glint.
                col += _Tint.rgb * 0.08 * (sun.color * saturate(dot(N, sun.direction)) * sun.shadowAttenuation + TracksAmbient(N));
                half3 H = normalize(sun.direction + V);
                col += sun.color * pow(saturate(dot(N, H)), 120.0) * 3.0 * sun.shadowAttenuation;
                return half4(TracksHazed(col, i.positionWS, 1.0), 1.0);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
