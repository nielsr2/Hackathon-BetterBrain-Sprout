#ifndef TREE_PROPERTIES_INCLUDED
#define TREE_PROPERTIES_INCLUDED

// Shared by ProcTree/Bark and ProcTree/Leaf. Per-material values live in UnityPerMaterial;
// per-tree values (year, buffers) arrive through the renderer's MaterialPropertyBlock and are
// plain globals (a single tree per scene, so SRP-Batcher compatibility is not a goal).

TEXTURE2D(_BaseMap);       SAMPLER(sampler_BaseMap);
TEXTURE2D(_NormalMap);     SAMPLER(sampler_NormalMap);
TEXTURE2D(_ThicknessMap);  SAMPLER(sampler_ThicknessMap);

CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _BaseColor;
float  _NormalScale;
float  _Smoothness;
float4 _TransmissionColor;
float  _TransmissionStrength;
float  _TransmissionScatter;
float  _Cutoff;
float4 _HueVariationColor;
float  _HueVariation;
float4 _YoungBarkColor;
float  _YoungBarkRadius;       // m: thinner wood blends toward _YoungBarkColor
float  _BarkTiling;            // texture repeats per metre (along) and per metre of mature circumference
float4 _DoubleSidedConstants;  // GetDoubleSidedConstants() reads this when _DOUBLESIDED_ON
CBUFFER_END

// ---- Per tree (MaterialPropertyBlock)
float _TreeYear;               // current simulated year
float _PrevTreeYear;           // last frame's year (motion vectors)
float _DeathFadeYears;
float _LiveLeafCount;
float _KeyYears[16];

// ---- Global wind (TreeWind component)
float4 _TreeWindDirection;     // xz used
float  _TreeWindStrength;
float  _TreeWindSpeed;
float  _TreeLeafFlutter;
float  _TreeFlutterFreq;

#endif // TREE_PROPERTIES_INCLUDED
