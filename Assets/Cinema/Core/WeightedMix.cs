using UnityEngine;

namespace Cinema
{
    /// <summary>
    /// Accumulates weighted clip values for Timeline mixers. Angles are mixed as unit vectors so
    /// 350° and 10° blend through 0°, not 180°. Result is normalised by the total weight, so a
    /// lone clip easing in still reads its own value (gaps are left to the caller).
    /// </summary>
    public struct WeightedMix
    {
        public float totalWeight;
        float _sum;
        Vector2 _dir;
        Vector4 _vec;

        public void Add(float value, float weight)
        {
            _sum += value * weight;
            totalWeight += weight;
        }

        /// <summary>Use instead of <see cref="Add(float,float)"/> for angles in degrees.</summary>
        public void AddAngle(float degrees, float weight)
        {
            float r = degrees * Mathf.Deg2Rad;
            _dir += new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * weight;
            totalWeight += weight;
        }

        public void Add(Vector4 value, float weight)
        {
            _vec += value * weight;
            totalWeight += weight;
        }

        public bool HasValue => totalWeight > 1e-5f;
        public float Value => HasValue ? _sum / totalWeight : 0f;
        public float Angle => Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
        public Vector4 Vector => HasValue ? _vec / totalWeight : Vector4.zero;
    }
}
