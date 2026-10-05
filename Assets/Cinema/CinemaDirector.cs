using Nib.ProcTree;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cinema
{
    /// <summary>
    /// Master cinema controller. Moves the one real camera between virtual <see cref="CinemaShot"/>s
    /// with smooth blends, and in Auto runs the program
    /// Orbit (settles) → Seedling close-up → Medium (once the seedling outgrows the close-up)
    /// → Full tree (once it nears full size). Waits for the sequence's Tree phase before starting.
    /// Keys: F1 orbit · F2 close-up · F3 medium · F4 full tree (manual) · F5 back to auto.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(100)] // after the tree driver has set this frame's growth
    [AddComponentMenu("Cinema/Cinema Director")]
    public sealed class CinemaDirector : MonoBehaviour
    {
        public enum Mode { Auto, Manual }

        [Header("References")]
        [Tooltip("The camera that is moved; it keeps its own post stack.")]
        public Camera targetCamera;
        public ProceduralTree tree;
        [Tooltip("Optional: hold the orbit at its start until the sequence reaches its Tree phase.")]
        public SequenceController sequence;

        [Header("Shots")]
        public OrbitShot orbit;
        public TrackTreeShot closeUp;
        public TrackTreeShot medium;
        public TrackTreeShot fullTree;

        [Header("Auto program")]
        public Mode mode = Mode.Auto;
        [Tooltip("Seconds to hold the settled orbit before cutting in to the seedling.")]
        [Min(0f)] public float orbitHoldSeconds = 2f;
        [Tooltip("Visible tree height (m) at which the close-up hands over to the medium shot.")]
        [Min(0f)] public float closeUpMaxHeight = 1.5f;
        [Tooltip("Visible / mature height at which the medium shot hands over to the full-tree shot.")]
        [Range(0f, 1f)] public float fullTreeAtFraction = 0.6f;

        [Header("Blends (s, 0 = hard cut)")]
        [Min(0f)] public float orbitToCloseUpBlend = 3f;
        [Min(0f)] public float closeUpToMediumBlend = 3.5f;
        [Min(0f)] public float mediumToFullTreeBlend = 5f;
        [Tooltip("Blend used for manual shot changes.")]
        [Min(0f)] public float manualBlend = 1.5f;

        [Header("Camera safety")]
        [Tooltip("Minimum camera height above the active terrain (m).")]
        [Min(0f)] public float groundClearance = 0.12f;

        [Header("Edit mode")]
        [Tooltip("Outside Play mode, put the camera on Preview Shot so it can be framed in the Game view.")]
        public bool previewInEditMode;
        public Shot previewShot = Shot.Orbit;

        [Header("Keys")]
        public bool enableKeys = true;

        [Header("Live (read-only)")]
        public Shot current = Shot.Orbit;
        public float liveHeight;
        public float matureHeight;
        public float grownFraction;

        /// <summary>World bounds of the tree as drawn right now.</summary>
        public Bounds LiveBounds { get; private set; }
        /// <summary>World bounds of the full-grown tree.</summary>
        public Bounds MatureBounds { get; private set; }
        public float Aspect => targetCamera != null && targetCamera.aspect > 0f ? targetCamera.aspect : 16f / 9f;

        CinemaPose _from;
        float _blendElapsed, _blendSeconds;
        bool _started;
        object _cachedBake;
        float _cachedYear = float.NaN;
        Bounds _localLive;

        void OnEnable()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (tree == null) tree = FindAnyObjectByType<ProceduralTree>();
            if (sequence == null) sequence = FindAnyObjectByType<SequenceController>();
        }

        void Start()
        {
            if (!Application.isPlaying) return;
            current = Shot.Orbit;
            RefreshBounds();
            TickShots(0f, snap: true);
            CutTo(Shot.Orbit, 0f);
        }

        /// <summary>The shot component for <paramref name="s"/>.</summary>
        public CinemaShot Get(Shot s) => s switch
        {
            Shot.Orbit => orbit,
            Shot.CloseUp => closeUp,
            Shot.Medium => medium,
            _ => fullTree,
        };

        /// <summary>Switch to <paramref name="s"/>, blending from wherever the camera is now.</summary>
        public void CutTo(Shot s, float blendSeconds)
        {
            current = s;
            _from = CameraPose();
            _blendElapsed = 0f;
            _blendSeconds = blendSeconds;
            var shot = Get(s);
            if (shot != null) shot.Restart();
        }

        /// <summary>Manual override: hold <paramref name="s"/> until <see cref="ResumeAuto"/>.</summary>
        public void Select(Shot s)
        {
            mode = Mode.Manual;
            CutTo(s, manualBlend);
        }

        public void ResumeAuto()
        {
            if (mode == Mode.Auto) return;
            mode = Mode.Auto;
            BlendFromCamera(manualBlend);
        }

        // ── Timeline (CinemaTrack) ──────────────────────────────────────────────

        enum TimelineControl { None, Pose, Auto }
        TimelineControl _tl;
        CinemaPose _tlPose;

        /// <summary>True while a CinemaTrack is in charge of the camera.</summary>
        public bool TimelineDriven => _tl != TimelineControl.None;

        /// <summary>Evaluate one of the director's shots at clip-local <paramref name="time"/> (Timeline shot clips).</summary>
        public CinemaPose EvaluateShot(Shot s, float time, float dt, bool snap)
        {
            if (!Application.isPlaying) RefreshBounds();
            var shot = Get(s);
            if (shot == null) return CameraPose();
            shot.Evaluate(this, time, dt, snap);
            return shot.Pose;
        }

        /// <summary>Timeline clips: put the camera on <paramref name="pose"/> (already blended by the track).</summary>
        public void TimelineDrive(in CinemaPose pose, Shot? dominant)
        {
            _tl = TimelineControl.Pose;
            _tlPose = pose;
            if (dominant.HasValue) current = dominant.Value; // orbit/free clips keep the last shot for a later auto hand-over
            _started = true;
        }

        /// <summary>Timeline "Director Auto" clip: growth-based switching, starting from the current shot.</summary>
        public void TimelineAuto()
        {
            if (_tl == TimelineControl.Auto) return;
            _tl = TimelineControl.Auto;
            _started = true;
            if (Application.isPlaying) BlendFromCamera(manualBlend);
        }

        /// <summary>The timeline graph is gone; fall back to the director's own program.</summary>
        public void ReleaseTimeline()
        {
            _tl = TimelineControl.None;
            if (!Application.isPlaying)
                for (var s = Shot.Orbit; s <= Shot.FullTree; s++) if (Get(s) != null) Get(s).Restart();
        }

        /// <summary>Edit-mode scrubbing: put the camera where the timeline says, right now.</summary>
        public void ApplyTimelineNow()
        {
            RefreshBounds();
            if (_tl == TimelineControl.Pose) Apply(_tlPose);
            else if (_tl == TimelineControl.Auto)
            {
                var s = CinemaRules.NextAuto(Shot.CloseUp, AutoState());
                var shot = Get(s);
                if (shot == null) return;
                shot.Tick(this, 0f, live: false, snap: true);
                Apply(shot.Pose);
            }
        }

        // ── Frame ───────────────────────────────────────────────────────────────

        void LateUpdate()
        {
            RefreshBounds();

            if (!Application.isPlaying)
            {
                TickShots(0f, snap: true);
                if (!TimelineDriven && previewInEditMode && Get(previewShot) != null) Apply(Get(previewShot).Pose);
                return;
            }

            float dt = Time.deltaTime;
            HandleKeys();
            _blendElapsed += dt;

            if (mode == Mode.Auto && _tl == TimelineControl.Pose)
            {
                ApplyBlended(_tlPose);
                return;
            }

            // Hold the orbit at its first frame (under the intro video) until the tree phase begins.
            bool waiting = sequence != null && sequence.isActiveAndEnabled && sequence.phase == SequenceController.Phase.Intro;
            if (!waiting && !_started)
            {
                _started = true;
                CutTo(Shot.Orbit, 0f);
            }

            if (_started && mode == Mode.Auto)
            {
                var next = CinemaRules.NextAuto(current, AutoState());
                if (next != current) CutTo(next, BlendInto(next));
            }

            TickShots(dt, snap: false, liveShot: _started ? current : (Shot?)null);
            var target = Get(current);
            if (target != null) ApplyBlended(target.Pose);
        }

        AutoInputs AutoState() => new AutoInputs
        {
            orbitSettled = orbit == null || orbit.Settled,
            orbitHeldSeconds = orbit != null ? orbit.HeldSeconds : float.MaxValue,
            orbitHoldSeconds = orbitHoldSeconds,
            liveHeight = liveHeight,
            closeUpMaxHeight = closeUpMaxHeight,
            grownFraction = grownFraction,
            fullTreeAtFraction = fullTreeAtFraction,
        };

        void BlendFromCamera(float seconds)
        {
            _from = CameraPose();
            _blendElapsed = 0f;
            _blendSeconds = seconds;
        }

        void ApplyBlended(in CinemaPose target)
        {
            float w = CinemaRules.BlendWeight(_blendElapsed, _blendSeconds);
            Apply(w >= 1f ? target : CinemaPose.Lerp(_from, target, w));
        }

        float BlendInto(Shot s) => s switch
        {
            Shot.CloseUp => orbitToCloseUpBlend,
            Shot.Medium => closeUpToMediumBlend,
            Shot.FullTree => mediumToFullTreeBlend,
            _ => manualBlend,
        };

        void TickShots(float dt, bool snap, Shot? liveShot = null)
        {
            for (var s = Shot.Orbit; s <= Shot.FullTree; s++)
            {
                var shot = Get(s);
                if (shot != null) shot.Tick(this, s == liveShot ? dt : 0f, live: s == liveShot, snap);
            }
        }

        void RefreshBounds()
        {
            if (tree == null) return;
            var root = tree.transform.position;
            var bake = tree.Bake;
            if (bake == null)
            {
                LiveBounds = MatureBounds = new Bounds(root + Vector3.up * 0.5f, Vector3.one);
                liveHeight = 0f; matureHeight = 1f; grownFraction = 0f;
                return;
            }

            // Re-walk the segments only when the shown year (or the tree itself) changes.
            if (!ReferenceEquals(bake, _cachedBake) || !Mathf.Approximately(tree.Year, _cachedYear))
            {
                _cachedBake = bake;
                _cachedYear = tree.Year;
                if (!TreeFraming.LiveBounds(bake, tree.Year, out _localLive))
                    _localLive = new Bounds(Vector3.zero, Vector3.zero);
            }

            var m = tree.transform.localToWorldMatrix;
            var bmin = new Vector3(bake.boundsMin.X, bake.boundsMin.Y, bake.boundsMin.Z);
            var bmax = new Vector3(bake.boundsMax.X, bake.boundsMax.Y, bake.boundsMax.Z);
            MatureBounds = TreeFraming.ToWorld(new Bounds((bmin + bmax) * 0.5f, bmax - bmin), m);
            LiveBounds = TreeFraming.ToWorld(_localLive, m);

            matureHeight = Mathf.Max(0.01f, MatureBounds.max.y - root.y);
            liveHeight = Mathf.Max(0f, LiveBounds.max.y - root.y);
            grownFraction = Mathf.Clamp01(liveHeight / matureHeight);
        }

        CinemaPose CameraPose()
        {
            if (targetCamera == null) return default;
            var t = targetCamera.transform;
            return new CinemaPose
            {
                position = t.position,
                rotation = t.rotation,
                fieldOfView = targetCamera.fieldOfView,
                nearClip = targetCamera.nearClipPlane,
            };
        }

        void Apply(in CinemaPose p)
        {
            if (targetCamera == null) return;
            var pos = p.position;
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                float ground = terrain.SampleHeight(pos) + terrain.GetPosition().y + groundClearance;
                if (pos.y < ground) pos.y = ground;
            }
            targetCamera.transform.SetPositionAndRotation(pos, p.rotation);
            targetCamera.fieldOfView = p.fieldOfView;
            targetCamera.nearClipPlane = p.nearClip;
        }

        void HandleKeys()
        {
            var kb = Keyboard.current;
            if (!enableKeys || kb == null) return;
            if (kb.f1Key.wasPressedThisFrame) Select(Shot.Orbit);
            if (kb.f2Key.wasPressedThisFrame) Select(Shot.CloseUp);
            if (kb.f3Key.wasPressedThisFrame) Select(Shot.Medium);
            if (kb.f4Key.wasPressedThisFrame) Select(Shot.FullTree);
            if (kb.f5Key.wasPressedThisFrame) ResumeAuto();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireCube(LiveBounds.center, LiveBounds.size);
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
            Gizmos.DrawWireCube(MatureBounds.center, MatureBounds.size);
        }
    }
}
