using Gtec.Chain.Common.Templates.Utilities;
using Gtec.UnityInterface;
using UnityEngine;
using static Gtec.Chain.Common.Templates.DataAcquisitionUnit.DataAcquisitionUnit;

/// <summary>
/// The session's view of the g.tec rig: is the Unicorn connected, is the ERP pipeline ready, has the
/// initial calibration (ERP training) produced a classifier. g.tec raises these events off the main
/// thread, so they only set flags here. Also shows/hides the rig's own screen-space UI and can start
/// the training run by script.
/// </summary>
[AddComponentMenu("Session/g.tec Sync")]
public sealed class GtecSync : MonoBehaviour
{
    public Device device;
    public ERPParadigm paradigm;
    public ERPPipeline pipeline;
    [Tooltip("The rig's screen-space canvases (device bar, signal quality, paradigm buttons).")]
    public Canvas[] uiCanvases = System.Array.Empty<Canvas>();

    public bool Connected => _connected;
    public bool PipelineReady => _ready;
    public bool Calibrated => _calibrated;
    public bool ParadigmRunning => _running;
    public string CalibrationQuality { get; private set; } = "";

    volatile bool _connected, _ready, _calibrated, _running;

    void OnEnable()
    {
        if (device != null) device.OnDeviceStateChanged.AddListener(OnDeviceState);
        if (pipeline != null)
        {
            pipeline.OnPipelineStateChanged.AddListener(OnPipelineState);
            pipeline.OnCalibrationResult.AddListener(OnCalibration);
        }
        if (paradigm != null)
        {
            paradigm.OnParadigmStarted.AddListener(OnStarted);
            paradigm.OnParadigmStopped.AddListener(OnStopped);
        }
    }

    void OnDisable()
    {
        if (device != null) device.OnDeviceStateChanged.RemoveListener(OnDeviceState);
        if (pipeline != null)
        {
            pipeline.OnPipelineStateChanged.RemoveListener(OnPipelineState);
            pipeline.OnCalibrationResult.RemoveListener(OnCalibration);
        }
        if (paradigm != null)
        {
            paradigm.OnParadigmStarted.RemoveListener(OnStarted);
            paradigm.OnParadigmStopped.RemoveListener(OnStopped);
        }
    }

    public void ShowUi(bool on)
    {
        foreach (var c in uiCanvases) if (c != null) c.enabled = on;
    }

    /// <summary>Start the ERP training run (the initial calibration). The g.tec paradigm UI follows along.</summary>
    public void StartCalibration()
    {
        if (paradigm == null || _running) return;
        Debug.Log("[Sync] Starting initial calibration (ERP training).");
        paradigm.StartParadigm(ParadigmMode.Training);
    }

    /// <summary>Run the paradigm in selection mode with the classifier from the initial calibration.</summary>
    public void StartChoice()
    {
        if (paradigm == null || !_calibrated) return;
        if (_running) paradigm.StopParadigm();
        Debug.Log("[Sync] Starting the ERP choice (application mode).");
        paradigm.StartParadigm(ParadigmMode.Application);
    }

    public void StopParadigm()
    {
        if (paradigm != null && _running) paradigm.StopParadigm();
    }

    void OnDeviceState(States s)
    {
        if (s == States.Connected) _connected = true;
        if (s == States.Disconnected) { _connected = false; _ready = false; }
    }

    void OnPipelineState(PipelineState s) => _ready = s == PipelineState.Ready;
    void OnStarted() => _running = true;
    void OnStopped() => _running = false;

    void OnCalibration(ERPParadigm p, CalibrationResult result)
    {
        if (result == null) return;
        CalibrationQuality = result.CalibrationQuality.ToString();
        _calibrated = true;
    }
}
