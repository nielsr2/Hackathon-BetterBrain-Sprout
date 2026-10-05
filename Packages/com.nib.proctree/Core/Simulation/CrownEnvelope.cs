using System.Numerics;

namespace Nib.ProcTree.Core.Simulation
{
    /// <summary>
    /// The space the crown may occupy at a given age: an upright ellipsoid from
    /// <c>crownBaseFraction · height</c> up to <c>height</c>, with height and width scaled by the
    /// profile's by-age curves. Young = narrow and tall-ish; mature = the wide parkland dome.
    /// </summary>
    public sealed class CrownEnvelope
    {
        readonly TreeParams _p;

        /// <summary>Wraps (does not copy) the params.</summary>
        public CrownEnvelope(TreeParams p) { _p = p; }

        /// <summary>Envelope height and width (m) at <paramref name="age01"/>.</summary>
        public void Size(float age01, out float height, out float width)
        {
            height = _p.matureHeight * MathHelpers.Clamp01(_p.heightByAge.Evaluate(age01));
            width = _p.matureWidth * MathHelpers.Clamp01(_p.widthByAge.Evaluate(age01));
            // Never fully degenerate: a seedling still owns a little column of space.
            if (height < 0.05f) height = 0.05f;
            if (width < 0.03f) width = 0.03f;
        }

        /// <summary>Centre and radii of the ellipsoid at <paramref name="age01"/>.</summary>
        public void Ellipsoid(float age01, out Vector3 center, out Vector3 radii)
        {
            Size(age01, out float h, out float w);
            float bottom = _p.crownBaseFraction * h;
            float ry = (h - bottom) * 0.5f;
            center = new Vector3(0f, bottom + ry, 0f);
            radii = new Vector3(w * 0.5f, ry, w * 0.5f);
        }

        /// <summary>True if <paramref name="p"/> is inside the envelope at <paramref name="age01"/>.</summary>
        public bool Contains(Vector3 p, float age01)
        {
            Ellipsoid(age01, out var c, out var r);
            var d = (p - c) / r;
            return d.LengthSquared() <= 1f + 1e-4f;
        }

        /// <summary>Top of the envelope (m) at <paramref name="age01"/>.</summary>
        public float Top(float age01) { Size(age01, out float h, out _); return h; }
    }
}
