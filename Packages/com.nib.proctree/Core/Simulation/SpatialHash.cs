using System;
using System.Collections.Generic;
using System.Numerics;

namespace Nib.ProcTree.Core.Simulation
{
    /// <summary>
    /// Uniform-grid point index. Build sorts point indices by cell key (counting into reusable
    /// arrays), so rebuilding every simulated year does not churn the heap.
    /// </summary>
    public sealed class SpatialHash
    {
        readonly float _cell;
        readonly Dictionary<long, (int start, int count)> _cells = new Dictionary<long, (int, int)>();
        int[] _sorted = Array.Empty<int>();
        long[] _keys = Array.Empty<long>();
        Vector3[] _points = Array.Empty<Vector3>();

        /// <summary>Grid with cubic cells of edge <paramref name="cellSize"/> (m).</summary>
        public SpatialHash(float cellSize) { _cell = MathF.Max(1e-4f, cellSize); }

        static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

        int Cell(float v) => (int)MathF.Floor(v / _cell);

        /// <summary>Indexes the first <paramref name="count"/> points (array is referenced, not copied).</summary>
        public void Build(Vector3[] points, int count)
        {
            _points = points;
            if (_sorted.Length < count) { _sorted = new int[count]; _keys = new long[count]; }
            for (int i = 0; i < count; i++)
            {
                var p = points[i];
                _keys[i] = Key(Cell(p.X), Cell(p.Y), Cell(p.Z));
                _sorted[i] = i;
            }
            Array.Sort(_keys, _sorted, 0, count);
            _cells.Clear();
            int s = 0;
            while (s < count)
            {
                int e = s + 1;
                while (e < count && _keys[e] == _keys[s]) e++;
                _cells[_keys[s]] = (s, e - s);
                s = e;
            }
        }

        /// <summary>Appends indices of points within <paramref name="radius"/> of <paramref name="c"/>.</summary>
        public void Query(Vector3 c, float radius, List<int> results)
        {
            float r2 = radius * radius;
            int x0 = Cell(c.X - radius), x1 = Cell(c.X + radius);
            int y0 = Cell(c.Y - radius), y1 = Cell(c.Y + radius);
            int z0 = Cell(c.Z - radius), z1 = Cell(c.Z + radius);
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            {
                if (!_cells.TryGetValue(Key(x, y, z), out var span)) continue;
                for (int k = span.start; k < span.start + span.count; k++)
                {
                    int i = _sorted[k];
                    if (Vector3.DistanceSquared(_points[i], c) <= r2) results.Add(i);
                }
            }
        }
    }
}
