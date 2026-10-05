Shader "Hidden/Custom/Scanlines"
{
    HLSLINCLUDE

    #pragma target 4.5

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

    // HDRP custom post-processes receive the source as _InputTexture (set by Scanlines.cs).
    TEXTURE2D_X(_InputTexture);

    float _LineIntensity;
    float _LineDensity;
    float _ScrollSpeed;

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings o;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        // HDRP helpers handle the platform Y-flip that a hand-rolled vertexID triangle misses.
        o.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        o.uv = GetFullScreenTriangleTexCoord(input.vertexID);
        return o;
    }

    float4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = input.uv;

        // Load by pixel so RTHandle scaling / dynamic resolution can't misalign the read.
        float4 col = LOAD_TEXTURE2D_X(_InputTexture, uv * _ScreenSize.xy);

        float scan = sin((uv.y * _LineDensity) + (_Time.y * _ScrollSpeed));
        scan = scan * 0.5 + 0.5;

        col.rgb *= lerp(1.0, scan, _LineIntensity);

        return col;
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "Scanlines"

            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            ENDHLSL
        }
    }

    Fallback Off
}
