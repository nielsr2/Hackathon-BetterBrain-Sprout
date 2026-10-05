#ifndef TREE_DATA_COMMON_INCLUDED
#define TREE_DATA_COMMON_INCLUDED

// Include block mirrored from ProcFoliage FrondData.hlsl (verified in HDRP).
// Properties FIRST: MaterialUtilities' GetDoubleSidedConstants() references _DoubleSidedConstants.
#include "Packages/com.nib.proctree/Shaders/Include/TreeProperties.hlsl"

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Sampling/SampleUVMapping.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"
// EvaluateAmbientProbe (TreeLighting) lives here; required in every pass because TreeLighting
// is compiled even where it is not called.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AmbientProbe.hlsl"

#include "Packages/com.nib.proctree/Shaders/Include/TreeGrowth.hlsl"
#include "Packages/com.nib.proctree/Shaders/Include/TreeWind.hlsl"
#include "Packages/com.nib.proctree/Shaders/Include/TreeLighting.hlsl"

#endif // TREE_DATA_COMMON_INCLUDED
