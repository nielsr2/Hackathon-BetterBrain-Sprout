using System;
using System.Numerics;

namespace Nib.ProcTree.Core.Simulation
{
    /// <summary>
    /// Space markers sprinkled through time: each marker gets an appear-age and a position inside
    /// the envelope of that age. Early envelopes are small, so they are densely populated — a
    /// sapling has enough light samples to branch — while the mature crown is sampled sparsely.
    /// Markers are static; growing wood consumes them.
    /// </summary>
    public sealed class MarkerCloud
    {
        /// <summary>Marker positions (tree-local, m).</summary>
        public readonly Vector3[] Position;
        /// <summary>Age01 at which each marker becomes visible.</summary>
        public readonly float[] AppearAge;
        readonly bool[] _consumed;

        /// <summary>Number of markers.</summary>
        public int Count => Position.Length;

        /// <summary>Samples <c>p.markerCount</c> markers from <paramref name="rng"/>.</summary>
        public MarkerCloud(TreeParams p, SeededRng rng)
        {
            var env = new CrownEnvelope(p);
            int n = p.markerCount;
            Position = new Vector3[n];
            AppearAge = new float[n];
            _consumed = new bool[n];
            for (int i = 0; i < n; i++)
            {
                float age = MathF.Pow(rng.NextFloat(), p.markerAgeBias);
                env.Ellipsoid(age, out var c, out var r);
                var u = rng.InsideUnitSphere();
                Position[i] = c + u * r;
                AppearAge[i] = age;
            }
        }

        /// <summary>Visible = already appeared and not consumed.</summary>
        public bool Visible(int i, float age01) => !_consumed[i] && AppearAge[i] <= age01;

        /// <summary>Marks a marker as occupied by wood.</summary>
        public void Consume(int i) => _consumed[i] = true;

        /// <summary>True if the marker has been consumed.</summary>
        public bool IsConsumed(int i) => _consumed[i];
    }
}
