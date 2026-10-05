#include "Packages/com.nib.proctree/Shaders/Include/TreeDataCommon.hlsl"

// Bark mesh contract (Core/Meshing/TreeBakeData.cs BarkMeshArrays):
//   NORMAL = radial, TANGENT = around-ring direction,
//   UV0 = (angle01, arc length m), UV1 = (segmentId, fractionAlong),
//   UV2 = (radialScale, axisPhase), UV3 = (axisOrder, axis base mature radius m), COLOR = tint.

#define HAVE_MESH_MODIFICATION
AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
#if defined(ATTRIBUTES_NEED_TEXCOORD1) && defined(ATTRIBUTES_NEED_TEXCOORD2) && defined(ATTRIBUTES_NEED_TEXCOORD3) && defined(ATTRIBUTES_NEED_NORMAL)
    float year = TreeYearFor(timeParameters);
    uint si = (uint)(input.uv1.x + 0.5);
    float radius;
    float3 pos = TreeBarkPosition(si, input.uv1.y, input.uv2.x, input.normalOS, year, radius);
    pos += TreeWindOffset(pos, si, year, timeParameters.x);
    input.positionOS = pos;

    // Ridges are fixed per axis (from its mature circumference), so as the wood thickens the
    // same ridges spread apart — bark that splits wider with age, with no popping.
    float repeats = max(1.0, round(6.2831853 * input.uv3.y * _BarkTiling));
    input.uv0.xy = float2(input.uv0.x * repeats, input.uv0.y * _BarkTiling);
#ifdef ATTRIBUTES_NEED_COLOR
    input.color.a = 1.0 - saturate(radius / max(_YoungBarkRadius, 1e-4));   // youngness
#endif
#endif
    return input;
}

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput,
                              out SurfaceData surfaceData, out BuiltinData builtinData)
{
    float2 uv = input.texCoord0.xy;
    float4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
    float young = saturate(input.color.a);
    float3 albedo = lerp(baseSample.rgb * input.color.rgb, _YoungBarkColor.rgb, young);

    float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv),
                                        _NormalScale * (1.0 - young));
    float3 N = normalize(TransformTangentToWorld(normalTS, input.tangentToWorld));

#if SHADERPASS == SHADERPASS_FORWARD_UNLIT
    float3 lit = TreeEvaluateLighting(posInput, N, V, albedo, _Smoothness, 1.0, 0.0);
#else
    float3 lit = albedo;   // depth/shadow/motion never shade
#endif

    ZERO_INITIALIZE(SurfaceData, surfaceData);
    surfaceData.color = lit;
    ZERO_BUILTIN_INITIALIZE(builtinData);
    builtinData.opacity = 1.0;
}
