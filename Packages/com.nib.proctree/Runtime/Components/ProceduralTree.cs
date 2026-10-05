using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Nib.ProcTree.Core;
using Nib.ProcTree.Core.Meshing;
using Nib.ProcTree.Rendering;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Nib.ProcTree
{
    /// <summary>
    /// One procedurally simulated tree whose whole life is driven by <see cref="growth"/> (0 = nothing,
    /// just above 0 = seedling, 1 = mature crown). The tree is simulated once from
    /// <see cref="profile"/> + <see cref="seed"/> on a background thread; changing
    /// <see cref="growth"/> afterwards costs almost nothing, so it can be driven every frame by
    /// MIDI/OSC, Timeline, the Animator or scripts.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Nib/Proc Tree/Procedural Tree")]
    public sealed class ProceduralTree : MonoBehaviour
    {
        /// <summary>Species settings.</summary>
        public TreeProfile profile;
        /// <summary>Tree seed: same profile + seed = same tree, always.</summary>
        public int seed = 1;
        /// <summary>Life stage: 0 nothing, 1 mature. Animatable.</summary>
        [Range(0f, 1f)] public float growth = 1f;
        /// <summary>Bark material (ProcTree/Bark).</summary>
        public Material barkMaterial;
        /// <summary>Leaf material (ProcTree/Leaf).</summary>
        public Material leafMaterial;
        /// <summary>Cast shadows from bark and leaves.</summary>
        public bool castShadows = true;

        /// <summary>Same as <see cref="growth"/>, clamped.</summary>
        public float Growth { get => growth; set => growth = Mathf.Clamp01(value); }

        /// <summary>Simulated year currently shown.</summary>
        public float Year { get; private set; }
        /// <summary>Years in the current bake (0 if none).</summary>
        public int Years => _gpu != null ? _gpu.data.bake.years : 0;
        /// <summary>True while a background regeneration is running.</summary>
        public bool IsGenerating => _task != null;
        /// <summary>Background regeneration progress (0..1).</summary>
        public float Progress => _progress;
        /// <summary>Wall time of the last simulation + bake, in ms.</summary>
        public long LastGenerateMs { get; private set; }
        /// <summary>The bake currently drawn, or null.</summary>
        public TreeBakeData Bake => _gpu?.data.bake;
        /// <summary>Leaves drawn right now.</summary>
        public int LiveLeafCount => _gpu?.LiveLeafCount ?? 0;
        /// <summary>Raised on the main thread after a new tree has been swapped in.</summary>
        public event Action Generated;

        const string BarkChild = "Bark";
        const string LeavesChild = "Leaves";

        TreeGpu _gpu;
        Task<TreeRenderData> _task;
        CancellationTokenSource _cancel;
        ulong _taskHash;
        volatile float _progress;
        long _taskStarted;
        float _prevYear = -1f;
        float _lastPushedYear = float.NaN, _lastPushedPrev = float.NaN;
        MeshRenderer _barkRenderer, _leafRenderer;
        MeshFilter _barkFilter, _leafFilter;
        float _nextStaleCheck;

        void OnEnable()
        {
            EnsureChildren();
            RequestRegenerate(force: false);
        }

        void OnDisable()
        {
            _cancel?.Cancel();
            _task = null;
            _gpu?.Dispose();
            _gpu = null;
            if (_barkFilter != null) _barkFilter.sharedMesh = null;
            if (_leafFilter != null) _leafFilter.sharedMesh = null;
            _lastPushedYear = float.NaN;
        }

        void OnValidate()
        {
            growth = Mathf.Clamp01(growth);
            if (isActiveAndEnabled) _nextStaleCheck = 0f;      // re-check staleness on the next update
        }

        /// <summary>Hash identifying what the current profile + seed would generate.</summary>
        public ulong CurrentSourceHash() => profile == null ? 0UL : profile.ToParams().ComputeHash() ^ ((ulong)(uint)seed * 0x9E3779B97F4A7C15UL);

        /// <summary>True if the drawn tree no longer matches the profile/seed.</summary>
        public bool IsStale => _gpu == null || _gpu.data.sourceHash != CurrentSourceHash();

        /// <summary>Starts a background regeneration (the current tree stays visible until it finishes).</summary>
        public void Regenerate() => RequestRegenerate(force: true);

        void RequestRegenerate(bool force)
        {
            if (profile == null) return;
            var p = profile.ToParams();                    // main thread: snapshots AnimationCurves
            int s = seed;
            ulong hash = p.ComputeHash() ^ ((ulong)(uint)s * 0x9E3779B97F4A7C15UL);
            if (!force && _gpu != null && _gpu.data.sourceHash == hash) return;
            if (_task != null && _taskHash == hash) return;

            _cancel?.Cancel();
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            _taskHash = hash;
            _progress = 0f;
            _taskStarted = Stopwatch.GetTimestamp();
            _task = Task.Run(() =>
            {
                var bake = TreeBaker.SimulateAndBake(p, s, f => _progress = f * 0.9f, token);
                token.ThrowIfCancellationRequested();
                var data = TreeRenderData.From(bake, hash);
                _progress = 1f;
                return data;
            }, token);
            KeepEditorTicking();
        }

        void Update()
        {
            PollTask();

#if UNITY_EDITOR
            if (!Application.isPlaying && Time.realtimeSinceStartup >= _nextStaleCheck)
            {
                _nextStaleCheck = Time.realtimeSinceStartup + 0.3f;
                if (_task == null && profile != null && IsStale) RequestRegenerate(force: false);
            }
#endif
            PushYear();
        }

        void PollTask()
        {
            if (_task == null || !_task.IsCompleted) { if (_task != null) KeepEditorTicking(); return; }
            var t = _task;
            _task = null;
            if (t.Status == TaskStatus.RanToCompletion && t.Result.sourceHash == _taskHash)
            {
                Swap(t.Result);
                LastGenerateMs = (Stopwatch.GetTimestamp() - _taskStarted) * 1000 / Stopwatch.Frequency;
                Generated?.Invoke();
            }
            else if (t.IsFaulted && !(t.Exception?.InnerException is OperationCanceledException))
            {
                Debug.LogException(t.Exception?.InnerException ?? t.Exception, this);
            }
        }

        void Swap(TreeRenderData data)
        {
            EnsureChildren();
            var old = _gpu;
            _gpu = new TreeGpu(data);
            _barkFilter.sharedMesh = _gpu.barkMesh;
            _leafFilter.sharedMesh = _gpu.leafMesh;
            old?.Dispose();
            _lastPushedYear = float.NaN;
            PushYear();
        }

        void PushYear()
        {
            if (_gpu == null || profile == null) return;
            float year = profile.YearFor(growth, _gpu.data.bake.years);
            float prev = _prevYear < 0f ? year : _prevYear;
            Year = year;
            ApplyRendererSettings();
            if (year != _lastPushedYear || prev != _lastPushedPrev)
            {
                _gpu.SetYear(year, prev, _barkRenderer, _leafRenderer);
                _lastPushedYear = year;
                _lastPushedPrev = prev;
            }
            _prevYear = year;
        }

        void ApplyRendererSettings()
        {
            if (_barkRenderer.sharedMaterial != barkMaterial) _barkRenderer.sharedMaterial = barkMaterial;
            if (_leafRenderer.sharedMaterial != leafMaterial) _leafRenderer.sharedMaterial = leafMaterial;
            var mode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (_barkRenderer.shadowCastingMode != mode) _barkRenderer.shadowCastingMode = mode;
            if (_leafRenderer.shadowCastingMode != mode) _leafRenderer.shadowCastingMode = mode;
        }

        // Child renderers are created on demand (so prefabs and existing scenes heal themselves) and
        // re-used afterwards; nothing here is rebuilt per frame.
        void EnsureChildren()
        {
            if (_barkRenderer == null) (_barkFilter, _barkRenderer) = Child(BarkChild);
            if (_leafRenderer == null) (_leafFilter, _leafRenderer) = Child(LeavesChild);
        }

        (MeshFilter, MeshRenderer) Child(string name)
        {
            var t = transform.Find(name);
            if (t == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                t = go.transform;
            }
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null) mf = t.gameObject.AddComponent<MeshFilter>();
            var mr = t.GetComponent<MeshRenderer>();
            if (mr == null) mr = t.gameObject.AddComponent<MeshRenderer>();
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            return (mf, mr);
        }

        void KeepEditorTicking()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }
    }
}
