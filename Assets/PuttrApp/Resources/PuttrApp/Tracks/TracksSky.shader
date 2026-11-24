// The sky over a Tracks course: a photographed panorama, drawn as the camera's skybox.
Shader "PuttrApp/TracksSky"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TracksCommon.hlsl"

            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(float4 positionOS : POSITION)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(positionOS.xyz);
                o.dir = positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(TracksSky(normalize(i.dir)), 1.0);
            }
            ENDHLSL
        }
    }
}
