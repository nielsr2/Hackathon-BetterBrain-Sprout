using Nib.ProcTree.Core;
using UnityEngine;

namespace Nib.ProcTree
{
    /// <summary>
    /// Species settings for a <see cref="ProceduralTree"/>: every simulation parameter plus the
    /// by-age curves (as editable <see cref="AnimationCurve"/>s) and the growth curve that maps the
    /// 0–1 slider to the tree's age. Ships as the Oak (Parkland) preset.
    /// </summary>
    [CreateAssetMenu(menuName = "Nib/Proc Tree/Tree Profile", fileName = "TreeProfile")]
    public sealed class TreeProfile : ScriptableObject
    {
        /// <summary>All scalar parameters. Its curve fields are overwritten by the AnimationCurves below.</summary>
        public TreeParams parameters = TreeParams.OakParkland();

        /// <summary>Slider (0..1) → age (0..1). Front-loaded so the sapling stage gets real slider travel.</summary>
        public AnimationCurve growthCurve = DefaultGrowthCurve();
        /// <summary>Crown envelope height scale over age.</summary>
        public AnimationCurve heightByAge = FromSampled(TreeParams.OakParkland().heightByAge);
        /// <summary>Crown envelope width scale over age.</summary>
        public AnimationCurve widthByAge = FromSampled(TreeParams.OakParkland().widthByAge);
        /// <summary>Internode length multiplier over age.</summary>
        public AnimationCurve internodeLengthByAge = FromSampled(TreeParams.OakParkland().internodeLengthByAge);
        /// <summary>Gravitropism over age (positive grows up, negative droops).</summary>
        public AnimationCurve tropismGravityByAge = FromSampled(TreeParams.OakParkland().tropismGravityByAge);

        /// <summary>Snapshots everything into thread-safe <see cref="TreeParams"/>. Main thread only.</summary>
        public TreeParams ToParams()
        {
            var p = (parameters ?? TreeParams.OakParkland()).Clone();
            p.heightByAge = Sample(heightByAge, p.heightByAge);
            p.widthByAge = Sample(widthByAge, p.widthByAge);
            p.internodeLengthByAge = Sample(internodeLengthByAge, p.internodeLengthByAge);
            p.tropismGravityByAge = Sample(tropismGravityByAge, p.tropismGravityByAge);
            p.Clamp();
            return p;
        }

        /// <summary>Simulated year shown at slider value <paramref name="growth"/>.</summary>
        public float YearFor(float growth, int years)
        {
            float g = Mathf.Clamp01(growth);
            float age = growthCurve != null && growthCurve.length > 0 ? Mathf.Clamp01(growthCurve.Evaluate(g)) : g;
            if (g <= 0f) age = 0f;
            return age * years;
        }

        /// <summary>Default slider → age mapping: age = growth^1.6.</summary>
        public static AnimationCurve DefaultGrowthCurve()
        {
            var keys = new Keyframe[9];
            for (int i = 0; i < keys.Length; i++)
            {
                float g = i / 8f;
                keys[i] = new Keyframe(g, Mathf.Pow(g, 1.6f));
            }
            var c = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        static SampledCurve Sample(AnimationCurve c, SampledCurve fallback)
        {
            if (c == null || c.length == 0) return fallback;
            const int n = 64;
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = c.Evaluate(i / (float)(n - 1));
            return new SampledCurve(s);
        }

        static AnimationCurve FromSampled(SampledCurve s)
        {
            const int keysCount = 9;
            var keys = new Keyframe[keysCount];
            for (int i = 0; i < keysCount; i++)
            {
                float t = i / (float)(keysCount - 1);
                keys[i] = new Keyframe(t, s.Evaluate(t));
            }
            var c = new AnimationCurve(keys);
            for (int i = 0; i < keysCount; i++) c.SmoothTangents(i, 0f);
            return c;
        }
    }
}
