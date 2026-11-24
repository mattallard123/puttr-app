// Trees and bushes on a Tracks course: cut-outs rendered from scanned models. Every tree is one card standing on its
// foot, turned to the camera (and leaning half way back toward a camera above it), and one card of its canopy seen from
// above, lying flat, that takes over when the camera looks straight down. Lit by the sun and sky from a dome-shaped
// normal map, shadowed, hazed, and thrown as a shadow by a card turned to the sun. A whole hole's trees are one mesh:
// the vertex carries the foot (position), the cut-out (uv), the corner's offset from the foot (uv2) and, in uv3,
// x: 0 standing card / 1 canopy from above, y: the canopy card's height, z: the tree's own shade,
// w: how far toward the sun its shadow is looked up (half its width).
Shader "PuttrApp/TracksTree"
{
    Properties
    {
        _Atlas ("Cut-outs", 2D) = "white" {}
        _AtlasN ("Cut-out normals", 2D) = "gray" {}
        _Unit ("World units in a yard", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _Atlas_ST, _AtlasN_ST;
        float _Unit;
        CBUFFER_END

        TEXTURE2D(_Atlas); SAMPLER(sampler_Atlas);
        TEXTURE2D(_AtlasN); SAMPLER(sampler_AtlasN);

        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 corner : TEXCOORD1; float4 kind : TEXCOORD2; };

        // A corner of a card in the world. facing: the unit vector from the tree to the eye (or to the sun, for its
        // shadow); lean: how far the standing card tips back to face an eye above it (0 upright, 1 square on).
        float3 Corner(Attributes v, float3 facing, float lean, out float3 right, out float3 up, out float3 toward, out float show)
        {
            float3 foot = TransformObjectToWorld(v.positionOS.xyz);
            float3 fh = float3(facing.x, 0.0, facing.z) / max(1e-4, length(facing.xz));
            float steep = abs(facing.y);
            if (v.kind.x > 0.5)
            {
                right = float3(1, 0, 0); up = float3(0, 0, 1); toward = float3(0, 1, 0);
                show = smoothstep(0.72, 0.9, steep);
                return foot + float3(v.corner.x, v.kind.y, v.corner.y);
            }
            float tip = facing.y * lean;
            float stand = sqrt(max(0.0, 1.0 - tip * tip));
            right = float3(fh.z, 0.0, -fh.x);
            up = float3(0.0, stand, 0.0) - fh * tip;
            toward = fh * stand + float3(0.0, tip, 0.0);
            show = 1.0 - smoothstep(0.9, 0.97, steep);
            return foot + right * v.corner.x + up * v.corner.y;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            AlphaToMask On
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "TracksCommon.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 right : TEXCOORD2;
                float3 up : TEXCOORD3;
                float3 toward : TEXCOORD4;
                float4 misc : TEXCOORD5;   // x: how much of the card shows, y: shade, z: shadow reach, w: 1 for the canopy card
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 foot = TransformObjectToWorld(v.positionOS.xyz);
                float3 facing = normalize(_WorldSpaceCameraPos - foot);
                float show;
                o.positionWS = Corner(v, facing, 0.5, o.right, o.up, o.toward, show);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                o.misc = float4(show, v.kind.z, v.kind.w, v.kind.x);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, i.uv);
                // A tree close in front of the camera steps out of the shot.
                float dist = distance(i.positionWS, _WorldSpaceCameraPos) / _Unit;
                half a = c.a * i.misc.x * smoothstep(14.0, 34.0, dist);
                a = saturate((a - 0.4) / max(fwidth(a), 0.0001) + 0.5);
                clip(a - 0.1);

                half3 n = SAMPLE_TEXTURE2D(_AtlasN, sampler_AtlasN, i.uv).rgb * 2.0 - 1.0;
                half3 N = normalize(i.right * n.x + i.up * n.y + i.toward * max(0.2, n.z));
                Light sky = GetMainLight();
                float3 from = i.positionWS + sky.direction * i.misc.z;   // clear of the tree's own shadow card
                Light sun = GetMainLight(TransformWorldToShadowCoord(from), from, half4(1, 1, 1, 1));
                // The canopy seen from above is the top of the tree: in the sun whatever stands beside it.
                sun.shadowAttenuation = lerp(sun.shadowAttenuation, 1.0, i.misc.w * 0.8);
                half3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                half wrap = saturate(dot(N, sun.direction) * 0.6 + 0.4);
                half through = pow(saturate(dot(-sun.direction, V)), 3.0) * 0.3;   // leaves with the sun behind them
                half3 col = c.rgb * i.misc.y * (sun.color * (wrap + through) * sun.shadowAttenuation + TracksAmbient(N));
                return half4(TracksHazed(col, i.positionWS, _Unit), a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 right, up, toward; float show;
                float3 positionWS = Corner(v, _LightDirection, 1.0, right, up, toward, show);
                if (v.kind.x > 0.5) positionWS = TransformObjectToWorld(v.positionOS.xyz);   // the canopy card throws none
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, _LightDirection, _LightDirection));
                #if UNITY_REVERSED_Z
                o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, i.uv).a - 0.4);
                return 0;
            }
            ENDHLSL
        }
    }
}
