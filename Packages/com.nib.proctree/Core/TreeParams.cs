using System;
using System.Reflection;

namespace Nib.ProcTree.Core
{
    /// <summary>
    /// Every simulation and meshing parameter, as plain data (spec §4). Units: metres, years,
    /// degrees. Built on the main thread from a <c>TreeProfile</c>, then only read by workers.
    /// </summary>
    [Serializable]
    public sealed class TreeParams
    {
        // ---- Time
        /// <summary>Simulated years; growth 1 = this age.</summary>
        public int years = 80;
        /// <summary>Fraction of each year during which that year's shoots extend.</summary>
        public float flushFraction = 0.4f;

        // ---- Crown envelope (mature size; scaled by age curves)
        /// <summary>Mature tree height (m).</summary>
        public float matureHeight = 14f;
        /// <summary>Mature crown width (m).</summary>
        public float matureWidth = 20f;
        /// <summary>Bottom of the crown envelope as a fraction of the current height.</summary>
        public float crownBaseFraction = 0.06f;
        /// <summary>Envelope height scale over age01.</summary>
        public SampledCurve heightByAge = SampledCurve.FromFunc(a => 1f - (1f - a) * (1f - a) * (1f - a));
        /// <summary>Envelope width scale over age01.</summary>
        public SampledCurve widthByAge = SampledCurve.FromFunc(a => MathF.Pow(a, 1.4f));

        // ---- Space colonisation
        /// <summary>Total space markers (light/space samples).</summary>
        public int markerCount = 60000;
        /// <summary>Marker appear-age = u^bias; above 1 puts more markers in early (small) envelopes.</summary>
        public float markerAgeBias = 1.0f;
        /// <summary>Full cone angle (deg) within which a bud perceives markers.</summary>
        public float perceptionAngle = 90f;
        /// <summary>Perception radius as a multiple of internode length.</summary>
        public float perceptionRadiusFactor = 6f;
        /// <summary>Marker consumption radius as a multiple of internode length.</summary>
        public float occupancyRadiusFactor = 2f;

        // ---- Vigor / extension
        /// <summary>Internode length (m) before the by-age factor.</summary>
        public float internodeLength = 0.12f;
        /// <summary>Internode length multiplier over age01 (young shoots are longer).</summary>
        public SampledCurve internodeLengthByAge = SampledCurve.FromFunc(a => 1.5f - 0.7f * a);
        /// <summary>Cap on internodes one bud can add in one year.</summary>
        public int maxInternodesPerYear = 8;
        /// <summary>Borchert–Honda alpha: resource at the root = vigorScale · Q(root).</summary>
        public float vigorScale = 2f;
        /// <summary>Borchert–Honda lambda: share favouring the main axis at each fork (0..1).</summary>
        public float apicalDominance = 0.45f;
        /// <summary>Light factor for lateral buds outside the terminal cluster.</summary>
        public float lateralLightFactor = 0.25f;
        /// <summary>Last N internodes of each shoot whose lateral buds count as the oak bud cluster.</summary>
        public int terminalClusterSize = 3;
        /// <summary>Extra light for the trunk leader while it is below the envelope top.</summary>
        public float apicalBaseLight = 6f;
        /// <summary>Age01 after which the leader loses its extra light and the crown turns decurrent (oak limbs take over).</summary>
        public float leaderDominanceAge = 0.3f;
        /// <summary>Years without light before a bud dies.</summary>
        public int budDeathYears = 3;

        // ---- Direction
        /// <summary>Weight of the previous heading.</summary>
        public float straightness = 1f;
        /// <summary>Weight of the free-space direction.</summary>
        public float tropismSpace = 0.3f;
        /// <summary>Gravitropism over age01: above 0 grows up, below 0 droops.</summary>
        public SampledCurve tropismGravityByAge = SampledCurve.FromFunc(a => 0.16f - 0.34f * a);
        /// <summary>Random wobble weight — the oak's gnarliness.</summary>
        public float gnarliness = 0.14f;
        /// <summary>Divergence angle between successive lateral buds (deg).</summary>
        public float phyllotaxisAngle = 144f;
        /// <summary>Angle between a lateral bud and its parent axis (deg).</summary>
        public float branchingAngle = 50f;

        // ---- Self-pruning
        /// <summary>A limb starves when its light per internode falls below this fraction of the tree's average.</summary>
        public float pruneLightThreshold = 0.5f;
        /// <summary>Limbs holding more than this fraction of the tree's internodes are never shed.</summary>
        public float pruneProtectFraction = 0.15f;
        /// <summary>Consecutive starving years before an axis dies.</summary>
        public int pruneYears = 3;
        /// <summary>Length (m) of a dead limb's stub that stays (0 = none).</summary>
        public float pruneStubLength = 0.25f;
        /// <summary>Years over which a dead branch shrinks away.</summary>
        public float deathFadeYears = 1f;

        // ---- Leaves
        /// <summary>Mean leaves per internode (fractional = probabilistic).</summary>
        public float leavesPerInternode = 1.2f;
        /// <summary>Leaves stay while their internode is within this many internodes of the axis tip.</summary>
        public int leafZoneInternodes = 8;
        /// <summary>Minimum years a leaf lives, even if its shoot races past it.</summary>
        public float leafMinLifeYears = 1f;
        /// <summary>Years a leaf takes to unfold.</summary>
        public float leafFlushYears = 0.12f;
        /// <summary>Leaf blade length (m).</summary>
        public float leafSize = 0.11f;
        /// <summary>Relative leaf size jitter.</summary>
        public float leafSizeJitter = 0.25f;

        // ---- Seedling
        /// <summary>Height (m) of the first-year shoot.</summary>
        public float seedlingHeight = 0.14f;
        /// <summary>Leaves on the seedling.</summary>
        public int seedlingLeafCount = 3;

        // ---- Thickness
        /// <summary>Radius (m) of a single-tip twig.</summary>
        public float minTwigRadius = 0.004f;
        /// <summary>Pipe-model exponent: r = minTwigRadius · tips^(1/e).</summary>
        public float pipeExponent = 1.9f;
        /// <summary>Extra radius fraction at the trunk base.</summary>
        public float rootFlare = 0.6f;
        /// <summary>Height (m) over which the root flare fades.</summary>
        public float rootFlareHeight = 1.0f;

        // ---- Meshing
        /// <summary>Ring sides on the thinnest twigs.</summary>
        public int ringSidesMin = 3;
        /// <summary>Ring sides on the trunk.</summary>
        public int ringSidesMax = 24;
        /// <summary>Radius multiplier of a branch collar ring.</summary>
        public float collarFlare = 1.15f;

        /// <summary>The shipped Oak (Parkland) preset.</summary>
        public static TreeParams OakParkland() => new TreeParams();

        /// <summary>Repairs NaN, negative and out-of-range values in place.</summary>
        public void Clamp()
        {
            years = Math.Clamp(years, 1, 400);
            flushFraction = C(flushFraction, 0.05f, 1f, 0.4f);
            matureHeight = C(matureHeight, 0.5f, 100f, 15f);
            matureWidth = C(matureWidth, 0.5f, 100f, 18f);
            crownBaseFraction = C(crownBaseFraction, 0f, 0.8f, 0.12f);
            markerCount = Math.Clamp(markerCount, 100, 1_000_000);
            markerAgeBias = C(markerAgeBias, 0.2f, 5f, 1.6f);
            perceptionAngle = C(perceptionAngle, 10f, 180f, 90f);
            perceptionRadiusFactor = C(perceptionRadiusFactor, 1f, 50f, 6f);
            occupancyRadiusFactor = C(occupancyRadiusFactor, 0.5f, 20f, 2f);
            internodeLength = C(internodeLength, 0.005f, 2f, 0.12f);
            maxInternodesPerYear = Math.Clamp(maxInternodesPerYear, 1, 64);
            vigorScale = C(vigorScale, 0.01f, 100f, 2f);
            apicalDominance = C(apicalDominance, 0f, 1f, 0.55f);
            lateralLightFactor = C(lateralLightFactor, 0f, 1f, 0.25f);
            terminalClusterSize = Math.Clamp(terminalClusterSize, 1, 64);
            apicalBaseLight = C(apicalBaseLight, 0f, 1000f, 6f);
            leaderDominanceAge = C(leaderDominanceAge, 0f, 1f, 0.3f);
            budDeathYears = Math.Clamp(budDeathYears, 1, 100);
            straightness = C(straightness, 0f, 10f, 1f);
            tropismSpace = C(tropismSpace, 0f, 10f, 0.3f);
            gnarliness = C(gnarliness, 0f, 5f, 0.14f);
            phyllotaxisAngle = C(phyllotaxisAngle, 0f, 360f, 144f);
            branchingAngle = C(branchingAngle, 0f, 120f, 50f);
            pruneLightThreshold = C(pruneLightThreshold, 0f, 10f, 0.3f);
            pruneProtectFraction = C(pruneProtectFraction, 0f, 1f, 0.15f);
            pruneYears = Math.Clamp(pruneYears, 1, 100);
            pruneStubLength = C(pruneStubLength, 0f, 10f, 0.25f);
            deathFadeYears = C(deathFadeYears, 0.01f, 20f, 1f);
            leavesPerInternode = C(leavesPerInternode, 0f, 16f, 1.2f);
            leafZoneInternodes = Math.Clamp(leafZoneInternodes, 1, 256);
            leafMinLifeYears = C(leafMinLifeYears, 0.05f, 50f, 1f);
            leafFlushYears = C(leafFlushYears, 0.01f, 5f, 0.12f);
            leafSize = C(leafSize, 0.005f, 2f, 0.11f);
            leafSizeJitter = C(leafSizeJitter, 0f, 0.9f, 0.25f);
            seedlingHeight = C(seedlingHeight, 0.01f, 2f, 0.14f);
            seedlingLeafCount = Math.Clamp(seedlingLeafCount, 0, 16);
            minTwigRadius = C(minTwigRadius, 0.0005f, 0.1f, 0.004f);
            pipeExponent = C(pipeExponent, 1f, 4f, 1.9f);
            rootFlare = C(rootFlare, 0f, 5f, 0.6f);
            rootFlareHeight = C(rootFlareHeight, 0.01f, 10f, 1f);
            ringSidesMin = Math.Clamp(ringSidesMin, 3, 64);
            ringSidesMax = Math.Clamp(ringSidesMax, ringSidesMin, 64);
            collarFlare = C(collarFlare, 1f, 3f, 1.15f);
            heightByAge ??= SampledCurve.Constant(1f);
            widthByAge ??= SampledCurve.Constant(1f);
            internodeLengthByAge ??= SampledCurve.Constant(1f);
            tropismGravityByAge ??= SampledCurve.Constant(0f);
        }

        static float C(float v, float min, float max, float fallback)
            => float.IsNaN(v) || float.IsInfinity(v) ? fallback : Math.Clamp(v, min, max);

        /// <summary>Deep copy (curves included).</summary>
        public TreeParams Clone()
        {
            var c = (TreeParams)MemberwiseClone();
            c.heightByAge = heightByAge?.Clone();
            c.widthByAge = widthByAge?.Clone();
            c.internodeLengthByAge = internodeLengthByAge?.Clone();
            c.tropismGravityByAge = tropismGravityByAge?.Clone();
            return c;
        }

        /// <summary>Stable 64-bit FNV-1a over every field (by name order), used to detect stale bakes.</summary>
        public ulong ComputeHash()
        {
            ulong h = 14695981039346656037UL;
            var fields = typeof(TreeParams).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (var f in fields)
            {
                object v = f.GetValue(this);
                switch (v)
                {
                    case int i: h = Mix(h, (uint)i); break;
                    case float x: h = Mix(h, (uint)BitConverter.SingleToInt32Bits(x)); break;
                    case SampledCurve sc:
                        h = Mix(h, (uint)sc.samples.Length);
                        foreach (float s in sc.samples) h = Mix(h, (uint)BitConverter.SingleToInt32Bits(s));
                        break;
                }
            }
            return h;
        }

        static ulong Mix(ulong h, uint v)
        {
            for (int b = 0; b < 4; b++) { h ^= (v >> (b * 8)) & 0xFF; h *= 1099511628211UL; }
            return h;
        }
    }
}
