using System;
using System.Collections.Generic;
using System.Numerics;

namespace Nib.ProcTree.Core.Meshing
{
    /// <summary>
    /// Sweeps one continuous tube per axis through its segments (spec §5.1). Ring frames are
    /// parallel-transported; ring sides scale with the axis' mature base radius (trunk → twig);
    /// a child axis starts inside its parent with a slightly flared collar ring; every axis ends in
    /// a cap point. Every vertex records the segment it belongs to, so the shader can grow it.
    /// </summary>
    public static class BarkMeshBuilder
    {
        sealed class Buffers
        {
            public readonly List<Vector3> c = new List<Vector3>(), r = new List<Vector3>(), t = new List<Vector3>();
            public readonly List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>(), uv2 = new List<Vector2>(), uv3 = new List<Vector2>();
            public readonly List<Vector4> col = new List<Vector4>();
            public readonly List<int> idx = new List<int>();

            public int Add(Vector3 centre, Vector3 radial, Vector3 tangent, Vector2 a, Vector2 b, Vector2 d, Vector2 e, Vector4 colour)
            {
                c.Add(centre); r.Add(radial); t.Add(tangent);
                uv0.Add(a); uv1.Add(b); uv2.Add(d); uv3.Add(e); col.Add(colour);
                return c.Count - 1;
            }
        }

        /// <summary>Builds the bark arrays for <paramref name="h"/>.</summary>
        public static BarkMeshArrays Build(TreeHistory h, TreeParams p)
        {
            var buf = new Buffers();
            if (h.segments.Count == 0) return Empty();
            int last = TreeHistory.KeyCount - 1;
            float trunkR = MathF.Max(1e-5f, h.radiusKeys[0 * TreeHistory.KeyCount + last]);
            var chain = new List<int>();

            for (int a = 0; a < h.axes.Count; a++)
            {
                var axis = h.axes[a];
                if (axis.firstSegment < 0) continue;
                chain.Clear();
                for (int s = axis.firstSegment; s >= 0; s = h.segments[s].mainChild) chain.Add(s);

                float baseR = h.radiusKeys[axis.firstSegment * TreeHistory.KeyCount + last];
                int sides = (int)MathF.Round(p.ringSidesMin + (p.ringSidesMax - p.ringSidesMin) * MathHelpers.Clamp01(baseR / trunkR));
                sides = Math.Clamp(sides, p.ringSidesMin, p.ringSidesMax);

                // Bark tint jitter per axis (subtle), alpha unused.
                float tint = 0.92f + 0.16f * axis.phase;
                var colour = new Vector4(tint, tint, tint, 1f);

                var first = h.segments[chain[0]];
                var dir = MathHelpers.SafeNormalize(first.end - first.start, Vector3.UnitY);
                var normal = MathHelpers.AnyPerpendicular(dir);
                float arc = 0f;
                float collar = a == 0 ? 1f : p.collarFlare;

                int prevRing = Ring(buf, first.start, dir, normal, sides, chain[0], 0f, collar, axis, arc, baseR, colour);

                for (int i = 0; i < chain.Count; i++)
                {
                    var seg = h.segments[chain[i]];
                    var d = MathHelpers.SafeNormalize(seg.end - seg.start, dir);
                    var tangent = d;
                    if (i + 1 < chain.Count)
                    {
                        var nx = h.segments[chain[i + 1]];
                        tangent = MathHelpers.SafeNormalize(d + MathHelpers.SafeNormalize(nx.end - nx.start, d), d);
                    }
                    // Parallel transport: project the previous normal onto the new ring plane.
                    normal = MathHelpers.SafeNormalize(normal - tangent * Vector3.Dot(normal, tangent), MathHelpers.AnyPerpendicular(tangent));
                    arc += Vector3.Distance(seg.start, seg.end);
                    int ring = Ring(buf, seg.end, tangent, normal, sides, chain[i], 1f, 1f, axis, arc, baseR, colour);
                    Strip(buf, prevRing, ring, sides);
                    prevRing = ring;
                    dir = d;
                }

                // Cap: one point at the tip, radius 0 (radial = tangent so it stays unit length).
                var tipSeg = h.segments[chain[chain.Count - 1]];
                int cap = buf.Add(tipSeg.end, dir, dir, new Vector2(0.5f, arc), new Vector2(chain[chain.Count - 1], 1f),
                                  new Vector2(0f, axis.phase), new Vector2(axis.order, baseR), colour);
                for (int j = 0; j < sides; j++) { buf.idx.Add(prevRing + j); buf.idx.Add(cap); buf.idx.Add(prevRing + Next(j, sides)); }
            }

            return new BarkMeshArrays
            {
                centerline = buf.c.ToArray(), radial = buf.r.ToArray(), tangent = buf.t.ToArray(),
                uv0 = buf.uv0.ToArray(), uv1 = buf.uv1.ToArray(), uv2 = buf.uv2.ToArray(), uv3 = buf.uv3.ToArray(),
                color = buf.col.ToArray(), indices = buf.idx.ToArray(),
            };
        }

        static BarkMeshArrays Empty() => new BarkMeshArrays();

        // sides+1 vertices (duplicated seam for clean UVs). Returns the first vertex index.
        static int Ring(Buffers b, Vector3 centre, Vector3 tangent, Vector3 normal, int sides, int seg, float fraction,
                        float radialScale, Axis axis, float arc, float matureRadius, Vector4 colour)
        {
            var binormal = Vector3.Cross(tangent, normal);
            int start = b.c.Count;
            int count = sides + Seam(sides);
            for (int j = 0; j < count; j++)
            {
                float a = (float)j / sides * MathF.PI * 2f;
                var radial = Vector3.Normalize(normal * MathF.Cos(a) + binormal * MathF.Sin(a));
                b.Add(centre, radial, tangent, new Vector2((float)j / sides, arc), new Vector2(seg, fraction),
                      new Vector2(radialScale, axis.phase), new Vector2(axis.order, matureRadius), colour);
            }
            return start;
        }

        // Thin rings skip the duplicated UV seam vertex: on a 3-4 sided twig the seam is invisible
        // and it saves a quarter of the twig vertices.
        static int Seam(int sides) => sides > 4 ? 1 : 0;

        static int Next(int j, int sides) => Seam(sides) == 1 ? j + 1 : (j + 1) % sides;

        static void Strip(Buffers b, int ringA, int ringB, int sides)
        {
            for (int j = 0; j < sides; j++)
            {
                int jn = Next(j, sides);
                int i0 = ringA + j, i1 = ringA + jn, i2 = ringB + j, i3 = ringB + jn;
                b.idx.Add(i0); b.idx.Add(i2); b.idx.Add(i1);
                b.idx.Add(i1); b.idx.Add(i2); b.idx.Add(i3);
            }
        }
    }
}
