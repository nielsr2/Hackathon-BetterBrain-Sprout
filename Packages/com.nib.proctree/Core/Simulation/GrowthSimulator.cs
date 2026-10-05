using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Nib.ProcTree.Core.Simulation
{
    /// <summary>
    /// Year-by-year bud-driven growth (Palubicki et al. 2009, spec §4): space markers give buds
    /// light, light flows to the root and back out as vigor (Borchert–Honda), vigorous buds extend
    /// shoots, starving limbs are shed, and young shoots carry leaves. Every piece of wood and every
    /// leaf records when it was born and when it died, so any age can be replayed later.
    /// Pure C#, no Unity API — safe on a worker thread.
    /// </summary>
    public static class GrowthSimulator
    {
        sealed class Bud
        {
            public Vector3 pos, dir;
            public int segment;      // segment whose end the bud sits on (-1 = ground)
            public int axis;
            public bool terminal;    // terminal buds continue their axis; laterals start a new one
            public bool cluster;     // oak terminal bud cluster (full light weight)
            public bool alive = true;
            public int starve;
            public float q, v;
            public Vector3 opt;      // sum of directions toward perceived markers
            public SeededRng rng;
            public int next;         // linked list of buds per segment (rebuilt yearly)
        }

        /// <summary>Simulates <c>p.years</c> years from <paramref name="seed"/>.</summary>
        /// <param name="source">Parameters; cloned and clamped, never modified.</param>
        /// <param name="seed">Tree seed.</param>
        /// <param name="progress">Optional 0..1 progress callback (worker thread).</param>
        /// <param name="cancel">Cancels between years; throws <see cref="OperationCanceledException"/>.</param>
        public static TreeHistory Run(TreeParams source, int seed, Action<float> progress = null,
                                      CancellationToken cancel = default)
        {
            var p = source.Clone();
            p.Clamp();
            var sim = new State(p, seed);
            for (int y = 1; y <= p.years; y++)
            {
                cancel.ThrowIfCancellationRequested();
                sim.Year(y);
                progress?.Invoke((float)y / p.years * 0.95f);
            }
            sim.Finish();
            progress?.Invoke(1f);
            return sim.History;
        }

        sealed class State
        {
            readonly TreeParams p;
            readonly CrownEnvelope env;
            readonly MarkerCloud markers;
            readonly int[] markerOrder;               // by appear age
            readonly SpatialHash markerHash;
            readonly SpatialHash budHash;
            readonly List<Bud> buds = new List<Bud>();
            readonly List<int> tmp = new List<int>();
            public readonly TreeHistory History;
            readonly List<Segment> segs;
            readonly List<Axis> axes;

            // Per-segment / per-axis side data (parallel lists).
            readonly List<int> firstLateral = new List<int>();   // first lateral child segment
            readonly List<int> nextSibling = new List<int>();    // next lateral sibling
            readonly List<float> axisPhyllo = new List<float>();
            readonly List<int> axisStarve = new List<int>();
            readonly List<float> axisBirth = new List<float>();

            float[] segQ = Array.Empty<float>();
            float[] vIn = Array.Empty<float>();
            int[] subtreeCount = Array.Empty<int>();
            int[] budHead = Array.Empty<int>();
            Vector3[] budPos = Array.Empty<Vector3>();
            int[] budIndex = Array.Empty<int>();

            readonly float perceptionR, occupancyR, cosHalfCone;

            public State(TreeParams p, int seed)
            {
                this.p = p;
                env = new CrownEnvelope(p);
                var root = new SeededRng(seed);
                markers = new MarkerCloud(p, root.Fork(1));
                markerOrder = new int[markers.Count];
                for (int i = 0; i < markerOrder.Length; i++) markerOrder[i] = i;
                var ages = (float[])markers.AppearAge.Clone();
                Array.Sort(ages, markerOrder);

                perceptionR = p.perceptionRadiusFactor * p.internodeLength;
                occupancyR = p.occupancyRadiusFactor * p.internodeLength;
                cosHalfCone = MathF.Cos(p.perceptionAngle * 0.5f * MathF.PI / 180f);
                markerHash = new SpatialHash(MathF.Max(occupancyR, 0.05f) * 2f);
                markerHash.Build(markers.Position, markers.Count);
                budHash = new SpatialHash(perceptionR);

                History = new TreeHistory { years = p.years, KeyYears = TreeHistory.BuildKeyYears(p.years) };
                segs = History.segments;
                axes = History.axes;

                var rootRng = root.Fork(2);
                AddAxis(-1, 0, 0f, rootRng.NextFloat());
                buds.Add(new Bud { pos = Vector3.Zero, dir = Vector3.UnitY, segment = -1, axis = 0, terminal = true, rng = rootRng });
            }

            int AddAxis(int parentSegment, int order, float birth, float phase)
            {
                axes.Add(new Axis { firstSegment = -1, parentSegment = parentSegment, order = order, death = float.PositiveInfinity, phase = phase });
                axisPhyllo.Add(0f);
                axisStarve.Add(0);
                axisBirth.Add(birth);
                return axes.Count - 1;
            }

            // ------------------------------------------------------------------ one year
            public void Year(int y)
            {
                float age = (float)y / p.years;
                if (y == 1) { GrowSeedling(); return; }

                EnsureCapacity();
                Light(age);
                BasipetalAndVigor(age);
                Grow(y, age);
                Prune(y);
            }

            void EnsureCapacity()
            {
                int n = segs.Count;
                if (segQ.Length < n)
                {
                    int cap = Math.Max(n * 2, 1024);
                    Array.Resize(ref segQ, cap);
                    Array.Resize(ref vIn, cap);
                    Array.Resize(ref subtreeCount, cap);
                    Array.Resize(ref budHead, cap);
                }
            }

            // Each visible marker lights the closest alive bud that perceives it.
            void Light(float age)
            {
                int alive = 0;
                if (budPos.Length < buds.Count) { budPos = new Vector3[buds.Count * 2]; budIndex = new int[buds.Count * 2]; }
                for (int i = 0; i < buds.Count; i++)
                {
                    var b = buds[i];
                    b.q = 0f; b.v = 0f; b.opt = Vector3.Zero;
                    if (!b.alive) continue;
                    budPos[alive] = b.pos;
                    budIndex[alive] = i;
                    alive++;
                }
                budHash.Build(budPos, alive);

                for (int oi = 0; oi < markerOrder.Length; oi++)
                {
                    int m = markerOrder[oi];
                    if (markers.AppearAge[m] > age) break;
                    if (markers.IsConsumed(m)) continue;
                    var mp = markers.Position[m];
                    tmp.Clear();
                    budHash.Query(mp, perceptionR, tmp);
                    int best = -1; float bestD = float.MaxValue;
                    for (int k = 0; k < tmp.Count; k++)
                    {
                        var b = buds[budIndex[tmp[k]]];
                        var d = mp - b.pos;
                        float dl = d.Length();
                        if (dl < 1e-5f) continue;
                        if (Vector3.Dot(d / dl, b.dir) < cosHalfCone) continue;
                        if (dl < bestD) { bestD = dl; best = budIndex[tmp[k]]; }
                    }
                    if (best < 0) continue;
                    var bb = buds[best];
                    bb.q += 1f;
                    bb.opt += (mp - bb.pos) / bestD;
                }

                float top = env.Top(age);
                foreach (var b in buds)
                {
                    if (!b.alive) continue;
                    if (!b.terminal && !b.cluster) b.q *= p.lateralLightFactor;
                    if (b.axis == 0 && b.terminal && age <= p.leaderDominanceAge && b.pos.Y < top * 0.97f) b.q += p.apicalBaseLight;
                    if (b.q <= 0f) { if (++b.starve >= p.budDeathYears) b.alive = false; }
                    else b.starve = 0;
                }
            }

            // Light flows tip -> root; vigor flows root -> tips, favouring the main axis (lambda).
            void BasipetalAndVigor(float age)
            {
                int n = segs.Count;
                for (int s = 0; s < n; s++) { segQ[s] = 0f; vIn[s] = 0f; budHead[s] = -1; subtreeCount[s] = 0; }
                for (int i = buds.Count - 1; i >= 0; i--)
                {
                    var b = buds[i];
                    if (!b.alive || b.segment < 0) continue;
                    b.next = budHead[b.segment];
                    budHead[b.segment] = i;
                    segQ[b.segment] += b.q;
                }
                for (int s = n - 1; s >= 0; s--)
                {
                    if (!IsAlive(s)) continue;
                    subtreeCount[s] += 1;
                    int par = segs[s].parent;
                    if (par >= 0) { segQ[par] += segQ[s]; subtreeCount[par] += subtreeCount[s]; }
                }

                if (n == 0) return;
                vIn[0] = p.vigorScale * segQ[0];
                float lambda = p.apicalDominance;
                for (int s = 0; s < n; s++)
                {
                    if (!IsAlive(s) || vIn[s] <= 0f) continue;
                    // Main: continuation segment, else this segment's terminal bud.
                    float qm = 0f; Bud mainBud = null;
                    int mc = segs[s].mainChild;
                    if (mc >= 0 && IsAlive(mc)) qm = segQ[mc];
                    else
                    {
                        for (int bi = budHead[s]; bi >= 0; bi = buds[bi].next)
                            if (buds[bi].terminal) { mainBud = buds[bi]; qm = mainBud.q; break; }
                    }
                    float ql = 0f;
                    for (int c = firstLateral[s]; c >= 0; c = nextSibling[c]) if (IsAlive(c)) ql += segQ[c];
                    for (int bi = budHead[s]; bi >= 0; bi = buds[bi].next) if (!buds[bi].terminal) ql += buds[bi].q;

                    float denom = lambda * qm + (1f - lambda) * ql;
                    if (denom <= 0f) continue;
                    float v = vIn[s];
                    float vm = v * lambda * qm / denom;
                    if (mc >= 0 && IsAlive(mc)) vIn[mc] += vm;
                    else if (mainBud != null) mainBud.v += vm;
                    float scaleL = v * (1f - lambda) / denom;
                    for (int c = firstLateral[s]; c >= 0; c = nextSibling[c]) if (IsAlive(c)) vIn[c] += scaleL * segQ[c];
                    for (int bi = budHead[s]; bi >= 0; bi = buds[bi].next) if (!buds[bi].terminal) buds[bi].v += scaleL * buds[bi].q;
                }
            }

            bool IsAlive(int s) => float.IsPositiveInfinity(segs[s].death);

            void Grow(int y, float age)
            {
                float len = p.internodeLength * MathF.Max(0.05f, p.internodeLengthByAge.Evaluate(age));
                int count = buds.Count;                 // buds created this year wait for next year
                for (int i = 0; i < count; i++)
                {
                    var b = buds[i];
                    if (!b.alive || b.v < 1f) continue;
                    int n = Math.Min((int)b.v, p.maxInternodesPerYear);
                    Shoot(b, n, len, y, age, seedling: false);
                }
            }

            void GrowSeedling()
            {
                var b = buds[0];
                int n = Math.Max(2, (int)MathF.Ceiling(p.seedlingHeight / (p.internodeLength * 0.5f)));
                Shoot(b, n, p.seedlingHeight / n, 1, 1f / p.years, seedling: true);
            }

            void Shoot(Bud b, int n, float len, int y, float age, bool seedling)
            {
                var rng = b.rng;
                Vector3 dir = b.dir;
                Vector3 opt = MathHelpers.SafeNormalize(b.opt, Vector3.Zero);
                float g = p.tropismGravityByAge.Evaluate(age);
                Vector3 start = b.pos;
                int prev = b.segment;
                int axis = b.axis;

                if (!b.terminal)
                {
                    axis = AddAxis(b.segment, axes[b.axis].order + 1, y - 1, rng.NextFloat());
                }

                float branchRad = p.branchingAngle * MathF.PI / 180f;
                for (int k = 0; k < n; k++)
                {
                    if (seedling)
                        dir = MathHelpers.SafeNormalize(dir + rng.UnitVector() * 0.04f, Vector3.UnitY);
                    else
                        dir = MathHelpers.SafeNormalize(dir * p.straightness + opt * p.tropismSpace
                                                        + Vector3.UnitY * g + rng.UnitVector() * p.gnarliness, Vector3.UnitY);
                    if (dir.Y < -0.6f) dir = MathHelpers.SafeNormalize(new Vector3(dir.X, -0.6f, dir.Z), Vector3.UnitY);

                    var end = start + dir * len;
                    if (end.Y < 0.02f) end.Y = 0.02f;   // never into the ground
                    int idx = segs.Count;
                    float flush = p.flushFraction;
                    segs.Add(new Segment
                    {
                        start = start, end = end, axis = axis, parent = prev, mainChild = -1,
                        birth = (y - 1) + flush * k / n, growDuration = flush / n,
                        death = float.PositiveInfinity,
                    });
                    firstLateral.Add(-1);
                    nextSibling.Add(-1);

                    if (k == 0 && !b.terminal)
                    {
                        var ax = axes[axis]; ax.firstSegment = idx; axes[axis] = ax;
                        nextSibling[idx] = firstLateral[prev];
                        firstLateral[prev] = idx;
                    }
                    else if (prev >= 0)
                    {
                        var ps = segs[prev]; ps.mainChild = idx; segs[prev] = ps;
                    }
                    else
                    {
                        var ax = axes[axis]; ax.firstSegment = idx; axes[axis] = ax;
                    }

                    Consume(end);

                    // Lateral bud at the node, rotated by phyllotaxis about the shoot.
                    axisPhyllo[axis] += p.phyllotaxisAngle * MathF.PI / 180f;
                    var perp = MathHelpers.Rotate(MathHelpers.AnyPerpendicular(dir), dir, axisPhyllo[axis]);
                    var ldir = MathHelpers.SafeNormalize(dir * MathF.Cos(branchRad) + perp * MathF.Sin(branchRad), dir);
                    buds.Add(new Bud
                    {
                        pos = end, dir = ldir, segment = idx, axis = axis, terminal = false,
                        cluster = k >= n - p.terminalClusterSize, rng = rng.Fork(idx),
                    });

                    if (seedling) { if (k >= n - p.seedlingLeafCount) AddLeaves(idx, 1, dir, perp, rng); }
                    else
                    {
                        float expected = p.leavesPerInternode * (0.4f + 1.2f * (k + 1) / n);
                        int c = (int)expected + (rng.Chance(expected - (int)expected) ? 1 : 0);
                        AddLeaves(idx, c, dir, perp, rng);
                    }

                    start = end;
                    prev = idx;
                }

                b.pos = start; b.dir = dir; b.segment = prev; b.axis = axis;
                b.terminal = true; b.cluster = false; b.starve = 0;
            }

            void Consume(Vector3 at)
            {
                tmp.Clear();
                markerHash.Query(at, occupancyR, tmp);
                for (int i = 0; i < tmp.Count; i++) markers.Consume(tmp[i]);
            }

            void AddLeaves(int seg, int count, Vector3 dir, Vector3 perp, SeededRng rng)
            {
                var s = segs[seg];
                for (int j = 0; j < count; j++)
                {
                    float around = j * MathF.PI * 0.8f + rng.Symmetric(0.6f);
                    var side = MathHelpers.Rotate(perp, dir, around);
                    var blade = MathHelpers.SafeNormalize(side * 0.8f + dir * 0.35f + Vector3.UnitY * 0.25f + rng.UnitVector() * 0.2f, side);
                    var up = Vector3.UnitY + rng.UnitVector() * 0.25f;
                    var normal = MathHelpers.SafeNormalize(up - blade * Vector3.Dot(up, blade), MathHelpers.AnyPerpendicular(blade));
                    var x = Vector3.Cross(normal, blade);
                    var m = new Matrix4x4(x.X, x.Y, x.Z, 0f, normal.X, normal.Y, normal.Z, 0f, blade.X, blade.Y, blade.Z, 0f, 0f, 0f, 0f, 1f);
                    float birth = s.GrownAt + rng.NextFloat() * 0.05f;
                    History.leaves.Add(new Leaf
                    {
                        position = s.end + side * 0.01f,
                        rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m)),
                        size = p.leafSize * (1f + rng.Symmetric(p.leafSizeJitter)),
                        birth = birth,
                        death = float.PositiveInfinity,          // resolved in Finish (tip zone)
                        segment = seg,
                        tint = rng.Symmetric(1f),
                        phase = rng.NextFloat(),
                    });
                }
            }

            // Limbs that stay starved of light for pruneYears die (except a short stub).
            // Relative to the tree's average light per internode, so a crown that has filled its
            // space (less light everywhere) does not shed everything at once; big scaffold limbs
            // are never shed.
            void Prune(int y)
            {
                if (segs.Count == 0 || subtreeCount[0] == 0) return;
                float average = segQ[0] / subtreeCount[0];
                int protect = (int)(p.pruneProtectFraction * subtreeCount[0]);
                for (int a = 1; a < axes.Count; a++)
                {
                    var ax = axes[a];
                    if (!float.IsPositiveInfinity(ax.death) || ax.firstSegment < 0) continue;
                    if (y - axisBirth[a] < 2f) continue;
                    int f = ax.firstSegment;
                    if (!IsAlive(f) || subtreeCount[f] == 0) continue;
                    if (subtreeCount[f] > protect) { axisStarve[a] = 0; continue; }
                    float ratio = segQ[f] / subtreeCount[f];
                    if (ratio < p.pruneLightThreshold * average) axisStarve[a]++;
                    else axisStarve[a] = 0;
                    if (axisStarve[a] >= p.pruneYears) Kill(a, y);
                }
                SweepAfterKills(y);
            }

            readonly Stack<int> stack = new Stack<int>();

            void Kill(int axis, int y)
            {
                float stubLeft = p.pruneStubLength;
                stack.Clear();
                stack.Push(axes[axis].firstSegment);
                while (stack.Count > 0)
                {
                    int s = stack.Pop();
                    var seg = segs[s];
                    if (!float.IsPositiveInfinity(seg.death)) continue;
                    bool stub = false;
                    if (seg.axis == axis && stubLeft > 0f)
                    {
                        stubLeft -= Vector3.Distance(seg.start, seg.end);
                        stub = true;
                    }
                    if (!stub) { seg.death = y; segs[s] = seg; }
                    for (int c = firstLateral[s]; c >= 0; c = nextSibling[c]) stack.Push(c);
                    if (seg.mainChild >= 0) stack.Push(seg.mainChild);
                }
                var killed = axes[axis]; killed.death = y; axes[axis] = killed;
                killedThisYear = true;
            }

            bool killedThisYear;

            // One pass after all of a year's kills: buds on dead wood (or on a killed limb's stub)
            // stop, and axes whose base died are dead too.
            void SweepAfterKills(int y)
            {
                if (!killedThisYear) return;
                killedThisYear = false;
                foreach (var b in buds)
                    if (b.alive && b.segment >= 0 && (!IsAlive(b.segment) || !float.IsPositiveInfinity(axes[b.axis].death)))
                        b.alive = false;
                for (int a = 0; a < axes.Count; a++)
                {
                    var ax = axes[a];
                    if (!float.IsPositiveInfinity(ax.death) || ax.firstSegment < 0) continue;
                    if (!IsAlive(ax.firstSegment)) { ax.death = y; axes[a] = ax; }
                }
            }

            // ------------------------------------------------------------------ finish
            public void Finish()
            {
                // A leaf lives while its internode is within leafZoneInternodes of its axis tip (at
                // least leafMinLifeYears), and drops when its limb dies (stubs included).
                var zoneEnd = new float[segs.Count];
                for (int s = 0; s < segs.Count; s++)
                {
                    int c = s;
                    for (int k = 0; k < p.leafZoneInternodes && c >= 0; k++) c = segs[c].mainChild;
                    zoneEnd[s] = c >= 0 ? segs[c].GrownAt : float.PositiveInfinity;
                }
                var leaves = History.leaves;
                for (int i = 0; i < leaves.Count; i++)
                {
                    var l = leaves[i];
                    float natural = MathF.Max(zoneEnd[l.segment] + 0.1f * l.phase, l.birth + p.leafMinLifeYears);
                    float limb = MathF.Min(segs[l.segment].death, axes[segs[l.segment].axis].death);
                    l.death = MathF.Min(natural, limb);
                    leaves[i] = l;
                }
                // A leaf whose wood died before it unfolded never existed.
                leaves.RemoveAll(l => l.death <= l.birth + 1e-3f);
                PipeModel.Compute(History, p);
                ComputeBounds();
            }

            void ComputeBounds()
            {
                var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
                for (int s = 0; s < segs.Count; s++)
                {
                    float r = History.RadiusAt(s, History.years);
                    var e = new Vector3(r);
                    mn = Vector3.Min(mn, Vector3.Min(segs[s].start, segs[s].end) - e);
                    mx = Vector3.Max(mx, Vector3.Max(segs[s].start, segs[s].end) + e);
                }
                foreach (var l in History.leaves)
                {
                    var e = new Vector3(l.size);
                    mn = Vector3.Min(mn, l.position - e);
                    mx = Vector3.Max(mx, l.position + e);
                }
                if (segs.Count == 0) { mn = Vector3.Zero; mx = Vector3.Zero; }
                History.boundsMin = mn; History.boundsMax = mx;
            }
        }
    }
}
