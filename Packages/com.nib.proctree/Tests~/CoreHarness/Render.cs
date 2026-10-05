using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Nib.ProcTree.Core;
using Nib.ProcTree.Core.Simulation;

namespace Nib.ProcTree.Harness
{
    /// <summary>
    /// Tuning aid: simulates the Oak and writes side + top silhouettes at several growth values
    /// (out/oak_seed{N}.png), plus a per-year stats table. Usage: --render [seed]
    /// </summary>
    static class Render
    {
        const int Panel = 320;

        public static int Run(string[] args)
        {
            int seed = args.Length > 0 ? int.Parse(args[0]) : 1;
            string tag = args.Length > 1 ? args[1] : "";
            float[] growths = { 0.03f, 0.1f, 0.25f, 0.45f, 0.7f, 1f };
            var p = TreeParams.OakParkland();
            var sw = Stopwatch.StartNew();
            var h = GrowthSimulator.Run(p, seed);
            Console.WriteLine($"simulated {h.years}y in {sw.ElapsedMilliseconds} ms: {h.segments.Count} segments, {h.axes.Count} axes, {h.leaves.Count} leaves total");
            Stats(h);

            int w = Panel * growths.Length, ht = Panel * 3;
            var img = new byte[w * ht * 3];
            for (int i = 0; i < img.Length; i++) img[i] = 245;
            float extent = MathF.Max(p.matureHeight, p.matureWidth) * 1.1f;
            for (int g = 0; g < growths.Length; g++)
            {
                float year = h.years * growths[g];
                DrawPanel(img, w, g * Panel, 0, h, year, extent, side: true);
                DrawPanel(img, w, g * Panel, Panel, h, year, extent, side: true, leaves: false);
                DrawPanel(img, w, g * Panel, Panel * 2, h, year, extent, side: false);
            }
            Directory.CreateDirectory("out");
            string path = Path.GetFullPath($"out/oak_seed{seed}{tag}.png");
            Png.Write(path, w, ht, img);
            Console.WriteLine($"wrote {path}");
            return 0;
        }

        static void Stats(TreeHistory h)
        {
            Console.WriteLine(" year  segs  axes  leaves  height  width  trunkD");
            for (int y = 1; y <= h.years; y += Math.Max(1, h.years / 16))
            {
                int segs = 0, leaves = 0;
                float top = 0f, rad = 0f;
                foreach (var s in h.segments)
                {
                    if (!(s.birth < y && y < s.death)) continue;
                    segs++;
                    top = MathF.Max(top, s.end.Y);
                    rad = MathF.Max(rad, new Vector2(s.end.X, s.end.Z).Length());
                }
                int axes = 0;
                foreach (var a in h.axes) if (a.firstSegment >= 0 && h.segments[a.firstSegment].birth < y && y < a.death) axes++;
                foreach (var l in h.leaves) if (l.birth < y && y < l.death) leaves++;
                float trunk = h.segments.Count > 0 ? 2f * h.RadiusAt(0, y) : 0f;
                Console.WriteLine($"{y,5} {segs,5} {axes,5} {leaves,7} {top,7:F2} {rad * 2,6:F2} {trunk,7:F3}");
            }
        }

        static void DrawPanel(byte[] img, int stride, int ox, int oy, TreeHistory h, float year, float extent, bool side, bool leaves = true)
        {
            float scale = Panel / extent;
            (float, float) Map(Vector3 v) => side
                ? (ox + Panel * 0.5f + v.X * scale, oy + Panel - 8 - v.Y * scale)
                : (ox + Panel * 0.5f + v.X * scale, oy + Panel * 0.5f + v.Z * scale);

            for (int s = 0; s < h.segments.Count; s++)
            {
                var seg = h.segments[s];
                if (year <= seg.birth || year >= seg.death) continue;
                float prog = MathHelpers.Clamp01((year - seg.birth) / seg.growDuration);
                var end = Vector3.Lerp(seg.start, seg.end, prog);
                float r = h.RadiusAt(s, year) * scale;
                var (x0, y0) = Map(seg.start); var (x1, y1) = Map(end);
                Line(img, stride, ox, oy, x0, y0, x1, y1, MathF.Max(0.5f, r), 90, 60, 35);
            }
            if (leaves) foreach (var l in h.leaves)
            {
                if (year <= l.birth || year >= l.death) continue;
                var (x, y) = Map(l.position);
                Dot(img, stride, ox, oy, x, y, MathF.Max(0.6f, l.size * scale * 0.5f), 70, 140, 50);
            }
        }

        static void Line(byte[] img, int stride, int ox, int oy, float x0, float y0, float x1, float y1, float r, byte cr, byte cg, byte cb)
        {
            float len = MathF.Max(MathF.Abs(x1 - x0), MathF.Abs(y1 - y0));
            int steps = Math.Max(1, (int)(len / MathF.Max(0.5f, r * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Dot(img, stride, ox, oy, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, r, cr, cg, cb);
            }
        }

        static void Dot(byte[] img, int stride, int ox, int oy, float cx, float cy, float r, byte cr, byte cg, byte cb)
        {
            int x0 = (int)(cx - r), x1 = (int)(cx + r), y0 = (int)(cy - r), y1 = (int)(cy + r);
            int height = img.Length / 3 / stride;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (x < ox || x >= ox + Panel || y < oy || y >= oy + Panel || y >= height) continue;
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                if (dx * dx + dy * dy > r * r + 0.25f) continue;
                int i = (y * stride + x) * 3;
                img[i] = cr; img[i + 1] = cg; img[i + 2] = cb;
            }
        }
    }
}
