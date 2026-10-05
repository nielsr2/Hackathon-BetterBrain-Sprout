using System;
using System.Collections.Generic;
using System.Numerics;

namespace Nib.ProcTree.Core.Meshing
{
    /// <summary>
    /// One low-poly oak leaf (spec §5.3): a 1 m blade, +Z along the midrib, +Y face normal, slight
    /// V-fold along the midrib and a gentle droop toward the tip. The lobed outline comes from the
    /// alpha-cut atlas; the mesh only needs to cover it.
    /// </summary>
    public static class LeafMeshBuilder
    {
        /// <summary>Builds the blade: <paramref name="rows"/> along the length × 3 columns (edge, midrib, edge).</summary>
        public static LeafMeshArrays Build(int rows = 6, float width = 0.62f, float fold = 0.08f, float droop = 0.12f)
        {
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            // A short petiole quad keeps the blade attached at the origin.
            for (int r = 0; r <= rows; r++)
            {
                float t = (float)r / rows;
                float z = t;
                float y = -droop * t * t;
                for (int c = 0; c < 3; c++)
                {
                    float u = c * 0.5f;                     // 0, 0.5, 1
                    float x = (u - 0.5f) * width;
                    float yy = y + (c == 1 ? fold * width * 0.5f : 0f);   // raised midrib
                    pos.Add(new Vector3(x, yy, z));
                    uv.Add(new Vector2(u, t));
                }
            }
            for (int r = 0; r <= rows; r++)
                for (int c = 0; c < 3; c++)
                    nrm.Add(Normal(pos, r, c, rows));
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < 2; c++)
                {
                    int i0 = r * 3 + c, i1 = i0 + 1, i2 = i0 + 3, i3 = i2 + 1;
                    idx.Add(i0); idx.Add(i2); idx.Add(i1);
                    idx.Add(i1); idx.Add(i2); idx.Add(i3);
                }
            return new LeafMeshArrays { positions = pos.ToArray(), normals = nrm.ToArray(), uv0 = uv.ToArray(), indices = idx.ToArray() };
        }

        static Vector3 Normal(List<Vector3> g, int r, int c, int rows)
        {
            int r0 = Math.Max(0, r - 1), r1 = Math.Min(rows, r + 1);
            int c0 = Math.Max(0, c - 1), c1 = Math.Min(2, c + 1);
            var dz = g[r1 * 3 + c] - g[r0 * 3 + c];
            var dx = g[r * 3 + c1] - g[r * 3 + c0];
            // Blend toward +Y: a rounded, light-friendly normal (same idea as ProcFoliage's bent normals).
            var n = MathHelpers.SafeNormalize(Vector3.Cross(dz, dx), Vector3.UnitY);
            return Vector3.Normalize(Vector3.Lerp(n, Vector3.UnitY, 0.4f));
        }
    }
}
