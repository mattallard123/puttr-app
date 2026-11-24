// Putt Star's crowd: thousands of people in one mesh, moved entirely on the GPU. Each vertex carries its person's
// feet (position), where it sits on them (an offset from a pivot: the shoulder for an arm, the feet otherwise) and
// who they are (phase, tempo, how keen). The game sets _State (time, hype, boo, pulse) once a frame; nothing else
// is touched on the CPU. Mode 0 draws the people as dark silhouettes rimmed by the stage light behind them; mode 1
// (additive) draws the phone torches held up in the crowd. It lives in Resources so builds include it.
Shader "PuttrArcade/PuttStarCrowd"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Base ("Silhouette", Color) = (0.006, 0.006, 0.009, 1)
        _Rim ("Rim", Color) = (1, 0.4, 0.8, 1)
        _Glow ("Phone glow", Color) = (1, 1, 1, 1)
        _Sweep ("Sweep x, x, width, strength", Vector) = (0, 0, 20, 0)
        _SweepColor ("Sweep color", Color) = (1, 1, 1, 1)
        _State ("Time, hype, boo, pulse", Vector) = (0, 0.5, 0, 0)
        _Mode ("0 people, 1 phones", Float) = 0
        _SrcBlend ("Src", Float) = 1
        _DstBlend ("Dst", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Base;
            half4 _Rim;
            half4 _Glow;
            half4 _SweepColor;
            float4 _Sweep;
            float4 _State;
            float _Mode;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;   // the person's feet
            float4 shape : TEXCOORD0;       // offset from the pivot (xy), the pivot from the feet (zw)
            float4 who : TEXCOORD1;         // phase, tempo, arm (-1 left, 0 body, 1 right), keen (negative: one arm up only)
            float4 more : TEXCOORD2;        // phone quad corner (xy), height, holds a phone up (1)
            float2 uv : TEXCOORD3;
            half4 color : COLOR;            // clothes (rgb), how near the outline (a)
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            float4 extra : TEXCOORD1;       // edge, height fraction, sweep, nearness to the stage
        };

        Varyings Animate(Attributes v)
        {
            float t = _State.x, hype = _State.y, boo = _State.z;
            float phase = v.who.x, rate = v.who.y, arm = v.who.z, keen = abs(v.who.w);
            float beat = max(0.0, sin(t * rate + phase));
            float jump = hype > 0.45 ? beat * (hype - 0.45) * 7.0 * keen : 0.0;
            float nod = sin(t * 4.0 + phase) * (0.3 + hype) * 0.2;
            float up = lerp(8.0, 165.0, saturate(hype * 1.15 * keen)) + sin(t * rate * 0.5 + phase) * 14.0 * hype;
            if (v.who.w < 0.0 && arm < 0.0) up = 10.0 + 8.0 * hype;                       // a drink in the other hand
            if (v.more.w > 0.5 && arm > 0.0) up = max(up, 135.0 + sin(t * 0.9 + phase) * 5.0); // filming
            float fist = phase > 3.0 ? 150.0 + sin(t * 15.0 + phase) * 14.0 : 14.0;
            if (boo > 0.2) up = lerp(up, fist, boo);
            float a = radians(up) * arm;
            float s = sin(a), c = cos(a);
            float2 off = v.shape.xy;
            float2 local = v.shape.zw + float2(off.x * c - off.y * s, off.x * s + off.y * c);
            if (_Mode > 0.5) local += v.more.xy;
            float sway = sin(t * 0.7 + phase) * 0.6 * (1.0 - boo);
            float3 p = v.positionOS.xyz + float3(local.x + sway, local.y + jump + nod, boo * 2.0);

            Varyings o;
            o.positionCS = TransformObjectToHClip(p);
            o.uv = v.uv;
            o.color = v.color;
            float x = v.positionOS.x;
            float da = (x - _Sweep.x) / _Sweep.z, db = (x - _Sweep.y) / _Sweep.z;
            float sweep = exp(-da * da) + exp(-db * db);
            o.extra = float4(v.color.a, saturate(local.y / max(1.0, v.more.z)), sweep, saturate(1.0 + v.positionOS.z / 125.0));
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "Crowd"
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            Varyings vert(Attributes v) { return Animate(v); }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _State.x, boo = _State.z, pulse = _State.w;
                if (_Mode > 0.5)
                {
                    half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                    float twinkle = 0.7 + 0.3 * sin(t * 2.3 + i.color.r * 400.0);
                    return half4(_Glow.rgb * a * twinkle * (1.0 - boo * 0.85), 1.0);
                }
                float edge = pow(saturate(i.extra.x), 14.0);
                float high = i.extra.y;
                float near = lerp(0.18, 1.0, i.extra.w * i.extra.w);
                half3 col = _Base.rgb + i.color.rgb;
                col += _Rim.rgb * edge * (0.25 + 0.75 * high) * near * (0.6 + 0.4 * pulse);
                col += _SweepColor.rgb * i.extra.z * _Sweep.w * (0.1 + 0.9 * high) * (0.006 + 0.994 * edge);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            Varyings vert(Attributes v) { return Animate(v); }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
