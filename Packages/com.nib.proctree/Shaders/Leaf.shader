Shader "ProcTree/Leaf"
{
    // Hand-written HDRP shader (no Shader Graph). Pass structure mirrors ProcFoliage's verified
    // FrondForward.shader: HDRP Unlit pass scaffolding + custom lighting (TreeLighting.hlsl).
    // The vertex stage replays growth from GPU buffers (TreeGrowth.hlsl), identically in every pass.
    Properties
    {
        _BaseMap("Leaf Atlas (alpha = lobes)", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Scale", Float) = 1.0
        _Smoothness("Smoothness", Range(0,1)) = 0.35
        _ThicknessMap("Thickness Map", 2D) = "white" {}
        _TransmissionColor("Transmission Color", Color) = (0.45,0.7,0.2,1)
        _TransmissionStrength("Transmission Strength", Range(0,4)) = 1.2
        _TransmissionScatter("Transmission Scatter", Range(0.1,8)) = 3.0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.45
        _HueVariationColor("Hue Variation Color", Color) = (0.55,0.6,0.25,1)
        _HueVariation("Hue Variation", Range(0,1)) = 0.3
        [HideInInspector] _ZClip("ZClip", Float) = 1.0
        [HideInInspector] _DoubleSidedConstants("_DoubleSidedConstants", Vector) = (1, 1, -1, 0)
        [HideInInspector] _MainTex("Albedo", 2D) = "white" {}
        [HideInInspector] _Color("Color", Color) = (1,1,1,1)
    }

    HLSLINCLUDE
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #pragma shader_feature_local _ALPHATEST_ON
    #pragma shader_feature_local _DOUBLESIDED_ON
    #pragma shader_feature_local _WIND_ON
    #pragma shader_feature_local_fragment _TRANSMISSION_ON
    #pragma shader_feature_local_fragment _HUE_VARIATION_ON

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
    #include "Packages/com.nib.proctree/Shaders/Include/TreeProperties.hlsl"
    ENDHLSL

    SubShader
    {
        Tags{ "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDUnlitShader" "Queue" = "AlphaTest+0" }

        Pass
        {
            Name "DepthForwardOnly"
            Tags{ "LightMode" = "DepthForwardOnly" }
            Cull Off
            AlphaToMask On
            ZWrite On

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #include "Packages/com.nib.proctree/Shaders/Include/TreeSharePass.hlsl"
            #include "Packages/com.nib.proctree/Shaders/Include/LeafData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags{ "LightMode" = "ShadowCaster" }
            Cull Off
            ZWrite On
            ZClip [_ZClip]
            ColorMask 0

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #define SHADERPASS SHADERPASS_SHADOWS

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #include "Packages/com.nib.proctree/Shaders/Include/TreeSharePass.hlsl"
            #include "Packages/com.nib.proctree/Shaders/Include/LeafData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags{ "LightMode" = "MotionVectors" }
            Cull Off
            AlphaToMask On
            ZWrite On

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #define SHADERPASS SHADERPASS_MOTION_VECTORS
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #include "Packages/com.nib.proctree/Shaders/Include/TreeSharePass.hlsl"
            #include "Packages/com.nib.proctree/Shaders/Include/LeafData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassMotionVectors.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "ForwardOnly"
            Tags{ "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #define SHADERPASS SHADERPASS_FORWARD_UNLIT
            #pragma multi_compile _ DEBUG_DISPLAY
            #define TREE_HAS_LIGHTLOOP
            #define USE_FPTL_LIGHTLIST   // culled punctual light list (as FrondForward)
            #ifdef DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
            #endif
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            // HDShadowAlgorithms needs a filter quality per shadow type (mirrors FrondForward).
            #define PUNCTUAL_SHADOW_MEDIUM
            #define DIRECTIONAL_SHADOW_MEDIUM
            #define AREA_SHADOW_MEDIUM
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AmbientProbe.hlsl"
            #include "Packages/com.nib.proctree/Shaders/Include/TreeSharePass.hlsl"
            #include "Packages/com.nib.proctree/Shaders/Include/LeafData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForwardUnlit.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
