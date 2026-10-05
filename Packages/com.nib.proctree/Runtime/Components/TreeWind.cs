using UnityEngine;

namespace Nib.ProcTree
{
    /// <summary>
    /// Ambient wind for Proc Tree shaders (spec §6.4): pushes shader globals, optionally follows a
    /// directional <see cref="WindZone"/>. Disabled ⇒ still air. Same pattern as ProcFoliage's
    /// FrondWind, with its own globals so the two packages stay independent.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Nib/Proc Tree/Tree Wind")]
    public sealed class TreeWind : MonoBehaviour
    {
        /// <summary>Wind direction (xz used).</summary>
        public Vector3 direction = new Vector3(1f, 0f, 0.3f);
        /// <summary>Overall strength.</summary>
        [Range(0f, 1f)] public float strength = 0.3f;
        /// <summary>Sway speed multiplier.</summary>
        public float speed = 1f;
        /// <summary>Leaf flutter amplitude (fraction of leaf size).</summary>
        public float leafFlutter = 0.08f;
        /// <summary>Leaf flutter frequency.</summary>
        public float flutterFrequency = 9f;
        /// <summary>If a directional WindZone exists, it overrides direction and strength.</summary>
        public bool followWindZone = true;

        static readonly int DirId = Shader.PropertyToID("_TreeWindDirection");
        static readonly int StrengthId = Shader.PropertyToID("_TreeWindStrength");
        static readonly int SpeedId = Shader.PropertyToID("_TreeWindSpeed");
        static readonly int FlutterId = Shader.PropertyToID("_TreeLeafFlutter");
        static readonly int FlutterFreqId = Shader.PropertyToID("_TreeFlutterFreq");

        WindZone _zone;

        void OnEnable() => Push(false);
        void OnValidate() => Push(false);
        void Update() => Push(followWindZone);

        /// <summary>Pushes the globals; scans for a WindZone only when <paramref name="allowZoneScan"/>.</summary>
        public void Push(bool allowZoneScan)
        {
            Vector3 dir = direction;
            float str = strength;
            if (followWindZone && allowZoneScan)
            {
                if (_zone == null) _zone = FindAnyObjectByType<WindZone>();
                if (_zone != null && _zone.mode == WindZoneMode.Directional)
                {
                    dir = _zone.transform.forward;
                    str = Mathf.Clamp01(_zone.windMain * 0.2f + _zone.windTurbulence * 0.1f);
                }
            }
            Shader.SetGlobalVector(DirId, new Vector4(dir.x, dir.y, dir.z, 0f));
            Shader.SetGlobalFloat(StrengthId, str);
            Shader.SetGlobalFloat(SpeedId, speed);
            Shader.SetGlobalFloat(FlutterId, leafFlutter);
            Shader.SetGlobalFloat(FlutterFreqId, flutterFrequency);
        }

        void OnDisable()
        {
            Shader.SetGlobalFloat(StrengthId, 0f);
            Shader.SetGlobalFloat(FlutterId, 0f);
        }
    }
}
