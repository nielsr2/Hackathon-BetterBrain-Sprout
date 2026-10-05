using System.Collections.Generic;
using Gtec.UnityInterface;
using Relaxation;
using UnityEngine;

/// <summary>
/// Lets the g.tec Device (which owns the Unicorn for the ERP flash tags) also feed the relaxation
/// loop: samples from an <see cref="EEGDataPipeline"/> are pushed into a
/// <see cref="UnicornBandReceiver"/> set to <c>Source.External</c>, so no Unicorn UDP app is needed
/// (it couldn't connect anyway; the headset takes one client at a time).
/// The block layout isn't documented: a dimension of 17 is taken as full Unicorn samples (same
/// layout as the UDP packet), 8 as EEG only. The first block is logged for checking.
/// </summary>
public class GtecEegBridge : MonoBehaviour
{
    public EEGDataPipeline pipeline;
    public UnicornBandReceiver receiver;
    [Tooltip("With no pipeline assigned, one is created under this (the g.tec Device's Pipelines) on Awake. " +
             "An EEGDataPipeline saved in a scene corrupts the built player's scene data (its UnityEvent<float[,]> " +
             "doesn't load in a player), so it must be made at runtime.")]
    public Transform pipelineParent;

    const int EegChannels = UnicornBandReceiver.Channels;
    const int Fields = UnicornBandReceiver.RawFields;

    readonly Queue<float[]> _pending = new Queue<float[]>();
    readonly object _lock = new object();
    bool _loggedFirst, _failed;
    float _counter;

    void Awake()
    {
        if (pipeline != null || pipelineParent == null) return;
        var go = new GameObject("EEGDataPipeline (runtime)") { layer = pipelineParent.gameObject.layer };
        go.SetActive(false); // configure before its Awake/OnEnable
        go.transform.SetParent(pipelineParent, false);
        pipeline = go.AddComponent<EEGDataPipeline>();
        pipeline.Mode = Gtec.Chain.Common.SignalProcessingPipelines.DataPipelineMode.Raw; // RawEegProcessor filters itself
        pipeline.UpdateRateHz = 25;
        // Unity's deserializer normally creates the UnityEvent fields; AddComponent leaves them null.
        foreach (var f in typeof(EEGDataPipeline).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            if (typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(f.FieldType) && f.GetValue(pipeline) == null)
                f.SetValue(pipeline, System.Activator.CreateInstance(f.FieldType));
        go.SetActive(true);
    }

    void OnEnable()
    {
        if (pipeline != null) pipeline.OnEEGDataAvailable.AddListener(OnData);
    }

    void OnDisable()
    {
        if (pipeline != null) pipeline.OnEEGDataAvailable.RemoveListener(OnData);
    }

    // May arrive off the main thread: only split and queue here.
    // The block is 2-D; whichever dimension is 17 (or 8) is taken as the per-sample fields.
    void OnData(float[,] data)
    {
        if (_failed || data == null || data.Length == 0) return;

        int d0 = data.GetLength(0), d1 = data.GetLength(1);
        bool rowsAreSamples = d1 == Fields || (d1 == EegChannels && d0 != Fields);
        int fields = rowsAreSamples ? d1 : d0;
        int samples = rowsAreSamples ? d0 : d1;
        bool ok = fields == Fields || fields == EegChannels;
        if (!_loggedFirst)
        {
            _loggedFirst = true;
            var first = new string[Mathf.Min(fields, Fields)];
            for (int f = 0; f < first.Length; f++) first[f] = (rowsAreSamples ? data[0, f] : data[f, 0]).ToString("0.##");
            Debug.Log($"[GtecEegBridge] First EEG block: [{d0}, {d1}], reading as {samples} samples × {fields} fields. " +
                      $"First sample: {string.Join(", ", first)}");
        }
        if (!ok)
        {
            _failed = true;
            Debug.LogError($"[GtecEegBridge] EEG block [{d0}, {d1}] has no dimension of {Fields} or {EegChannels}; not forwarding.");
            return;
        }

        lock (_lock)
        {
            for (int s = 0; s < samples; s++)
            {
                var sample = new float[Fields];
                for (int f = 0; f < fields; f++) sample[f] = rowsAreSamples ? data[s, f] : data[f, s];
                if (fields == EegChannels)
                {
                    sample[UnicornBandReceiver.RawBattery] = -1f; // unknown: hides battery in the overlay
                    sample[UnicornBandReceiver.RawCounter] = _counter++;
                }
                _pending.Enqueue(sample);
            }
        }
    }

    void Update()
    {
        if (receiver == null) return;
        lock (_lock)
        {
            while (_pending.Count > 0) receiver.PushRawSample(_pending.Dequeue());
        }
    }
}
