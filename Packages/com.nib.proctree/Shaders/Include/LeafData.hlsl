#include "Packages/com.nib.proctree/Shaders/Include/TreeDataCommon.hlsl"

// Leaf slot mesh contract: POSITION/NORMAL/TANGENT = the 1 m blade template (+Z length, +Y face),
// UV0 = atlas, UV1.x = slot index. Slot i draws leaf _LiveLeaves[i].

#define HAVE_MESH_MODIFICATION
AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
#if defined(ATTRIBUTES_NEED_TEXCOORD1) && defined(ATTRIBUTES_NEED_NORMAL) && defined(ATTRIBUTES_NEED_TANGENT)
    float year = TreeYearFor(timeParameters);
    uint slot = (uint)(input.uv1.x + 0.5);
    if (slot >= (uint)_LiveLeafCount)
    {
        input.positionOS = float3(0, 0, 0);    // outside the live range: degenerate
        return input;
    }
    LeafGpu leaf = _Leaves[_LiveLeaves[slot]];
    float along = saturate(input.positionOS.z);              // 0 petiole .. 1 tip
    float scale = TreeLeafScale(leaf, year) * leaf.size;

    float3 n = TreeRotate(leaf.rotation, input.normalOS);
    float3 pos = leaf.position + TreeRotate(leaf.rotation, input.positionOS * scale);
    uint si = (uint)(leaf.segment + 0.5);
    pos += TreeWindOffset(leaf.position, si, year, timeParameters.x);      // follow the twig
    pos += TreeLeafFlutter(n, leaf.phase, timeParameters.x) * along * scale;

    input.positionOS = pos;
    input.normalOS = n;
    input.tangentOS.xyz = TreeRotate(leaf.rotation, input.tangentOS.xyz);
#ifdef ATTRIBUTES_NEED_COLOR
    // 0.5-centred hue/value offsets, same convention as ProcFoliage.
    input.color = float4(0.5 + 0.5 * leaf.tint, 0.5 + 0.35 * (frac(leaf.phase * 7.13) - 0.5), 0.5, 0.0);
#endif
#endif
    return input;
}

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput,
                              out SurfaceData surfaceData, out BuiltinData builtinData)
{
    float2 uv = input.texCoord0.xy * _BaseMap_ST.xy + _BaseMap_ST.zw;
    float4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
    float alpha = baseSample.a;
#ifdef _ALPHATEST_ON
    clip(alpha - _Cutoff);
#endif

    float3 albedo = baseSample.rgb;
#ifdef _HUE_VARIATION_ON
    float hueShift = (input.color.r - 0.5) * 2.0 * _HueVariation;
    albedo = lerp(albedo, albedo * _HueVariationColor.rgb, saturate(abs(hueShift)));
    albedo *= 1.0 + (input.color.g - 0.5) * 2.0 * _HueVariation;
#endif

    float thickness = 1.0 - SAMPLE_TEXTURE2D(_ThicknessMap, sampler_ThicknessMap, uv).r;
    float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _NormalScale);
    float3 N = normalize(TransformTangentToWorld(normalTS, input.tangentToWorld));
#ifdef _DOUBLESIDED_ON
    N = input.isFrontFace ? N : -N;
#endif

#if SHADERPASS == SHADERPASS_FORWARD_UNLIT
    float3 lit = TreeEvaluateLighting(posInput, N, V, albedo, _Smoothness, 1.0, thickness);
#else
    float3 lit = albedo;
#endif

    ZERO_INITIALIZE(SurfaceData, surfaceData);
    surfaceData.color = lit;
    ZERO_BUILTIN_INITIALIZE(builtinData);
    builtinData.opacity = alpha;
#ifdef _ALPHATEST_ON
    builtinData.alphaClipTreshold = _Cutoff;
#endif
}
