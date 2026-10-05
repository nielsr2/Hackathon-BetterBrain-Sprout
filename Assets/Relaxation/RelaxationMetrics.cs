using UnityEngine;

namespace Relaxation
{
    /// <summary>EEG relaxation/meditation metrics. Order matches the 1–8 hotkeys.</summary>
    public enum RelaxationMetric
    {
        AlphaBeta,          // α/β — intuitive relaxation ratio
        AlphaThetaOverBeta, // (α+θ)/β — common neurofeedback ratio, recommended primary
        RelativeAlpha,      // α/total
        AlphaOverThetaBeta, // α/(θ+β)
        WeightedRelaxation, // (α+0.5θ)/β
        FrontalTheta,       // θ at Fz — focused-meditation marker
        ThetaAlpha,         // θ/α — also rises with drowsiness
        ThetaBeta,          // θ/β — not meditation-specific
    }

    public static class RelaxationMetrics
    {
        public const int Count = 8;
        const float Eps = 1e-6f;

        static readonly string[] Labels =
        {
            "Alpha / Beta", "(Alpha+Theta) / Beta", "Relative Alpha", "Alpha / (Theta+Beta)",
            "Relaxation Score", "Frontal Theta (Fz)", "Theta / Alpha", "Theta / Beta",
        };

        static readonly string[] Formulas =
        {
            "α/β", "(α+θ)/β", "α/total", "α/(θ+β)", "(α+0.5θ)/β", "θ@Fz", "θ/α", "θ/β",
        };

        public static string Label(RelaxationMetric m) => Labels[(int)m];
        public static string Formula(RelaxationMetric m) => Formulas[(int)m];

        public static float Compute(RelaxationMetric m, in BandPowers b)
        {
            switch (m)
            {
                case RelaxationMetric.AlphaBeta:          return Div(b.alpha, b.Beta);
                case RelaxationMetric.AlphaThetaOverBeta: return Div(b.alpha + b.theta, b.Beta);
                case RelaxationMetric.RelativeAlpha:      return Div(b.alpha, b.Total);
                case RelaxationMetric.AlphaOverThetaBeta: return Div(b.alpha, b.theta + b.Beta);
                case RelaxationMetric.WeightedRelaxation: return Div(b.alpha + 0.5f * b.theta, b.Beta);
                case RelaxationMetric.FrontalTheta:       return b.fzTheta;
                case RelaxationMetric.ThetaAlpha:         return Div(b.theta, b.alpha);
                case RelaxationMetric.ThetaBeta:          return Div(b.theta, b.Beta);
                default:                                  return 0f;
            }
        }

        /// <summary>Fills <paramref name="into"/> (length ≥ <see cref="Count"/>) with every metric.</summary>
        public static void ComputeAll(in BandPowers b, float[] into)
        {
            for (int i = 0; i < Count; i++) into[i] = Compute((RelaxationMetric)i, b);
        }

        // Band powers are non-negative; clamping the denominator keeps a dead channel from yielding ∞/NaN.
        static float Div(float num, float den) => num / Mathf.Max(den, Eps);
    }
}
