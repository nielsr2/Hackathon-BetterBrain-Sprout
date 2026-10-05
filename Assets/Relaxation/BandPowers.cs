using System;

namespace Relaxation
{
    /// <summary>
    /// One sample of Unicorn band powers (linear power, averaged over the 8 channels) plus
    /// theta at Fz for the frontal-midline metric.
    /// </summary>
    [Serializable]
    public struct BandPowers
    {
        public float delta, theta, alpha, betaLow, betaMid, betaHigh, gamma;
        public float fzTheta;

        /// <summary>Beta 13–30 Hz: the three Unicorn beta bands summed.</summary>
        public float Beta => betaLow + betaMid + betaHigh;

        /// <summary>Total power across all seven bands.</summary>
        public float Total => delta + theta + alpha + Beta + gamma;
    }
}
