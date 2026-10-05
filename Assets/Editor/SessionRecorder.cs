using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Records a whole <see cref="SessionFlow"/> run to <c>&lt;project&gt;/Recordings/Session_NNN.mp4</c>.
/// The session has no fixed length (it waits for connect, calibration, baseline and growth), so instead
/// of a Recorder clip on a timeline (the film takes) this drives the Recorder from script: start on
/// entering Play mode, stop when Play mode ends. The flow quits by itself
/// <see cref="SessionFlow.recordTailSeconds"/> after the final glitch. Same encoder as the film takes.
/// </summary>
public static class SessionRecorder
{
    const string Flag = "SessionRecorder.Recording";
    const string QuitWasKey = "SessionRecorder.QuitAtEndWas";
    const string FileKey = "SessionRecorder.File";
    const string Folder = "Recordings";

    static RecorderController _controller;

    [MenuItem("Tools/Session/Record Full Session")]
    public static void Record()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Record] Leave Play mode first."); return; }
        var flow = Object.FindAnyObjectByType<SessionFlow>();
        if (flow == null) { Debug.LogError("[Record] No SessionFlow in the open scene; open Assets/Session/oak_session.unity."); return; }

        // quitAtEnd only for this run; restored when Play mode ends.
        SessionState.SetBool(QuitWasKey, flow.quitAtEnd);
        flow.quitAtEnd = true;
        EditorUtility.SetDirty(flow);
        EditorSceneManager.SaveScene(flow.gameObject.scene);

        SessionState.SetBool(Flag, true);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Hook() => EditorApplication.playModeStateChanged += OnPlayMode;

    static void OnPlayMode(PlayModeStateChange s)
    {
        if (!SessionState.GetBool(Flag, false)) return;
        switch (s)
        {
            case PlayModeStateChange.EnteredPlayMode: Begin(); break;
            case PlayModeStateChange.ExitingPlayMode: End(); break;
            case PlayModeStateChange.EnteredEditMode: Finish(); break;
        }
    }

    static void Begin()
    {
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), Folder);
        Directory.CreateDirectory(dir);
        int take = 1;
        while (File.Exists(Path.Combine(dir, $"Session_{take:000}.mp4"))) take++;
        string file = $"Session_{take:000}";
        SessionState.SetString(FileKey, Path.Combine(dir, file + ".mp4"));

        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "Session Recorder";
        TimelineSetup.ConfigureRecorder(movie, file, OutputPath.Root.Project, Folder, audio: true);

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToManual();
        settings.FrameRate = movie.FrameRate;
        settings.FrameRatePlayback = FrameRatePlayback.Constant;
        settings.CapFrameRate = true;
        settings.ExitPlayMode = false; // SessionFlow leaves Play mode itself

        _controller = new RecorderController(settings);
        _controller.PrepareRecording();
        if (_controller.StartRecording()) Debug.Log($"[Record] Recording the session → {Folder}/{file}.mp4. It stops after the final glitch.");
        else Debug.LogError("[Record] The Recorder did not start; check the console for Recorder errors.");
    }

    static void End()
    {
        if (_controller != null && _controller.IsRecording()) _controller.StopRecording(); // finalises the MP4
        _controller = null;
    }

    static void Finish()
    {
        SessionState.SetBool(Flag, false);
        var flow = Object.FindAnyObjectByType<SessionFlow>();
        if (flow != null) { flow.quitAtEnd = SessionState.GetBool(QuitWasKey, false); EditorUtility.SetDirty(flow); }

        var info = new FileInfo(SessionState.GetString(FileKey, ""));
        if (info.Exists && info.Length > 0) Debug.Log($"[Record] Recording saved → {info.FullName} ({info.Length / 1e6f:0.0} MB).");
        else Debug.LogError($"[Record] Expected {info.FullName} but it is missing or empty — check the console for a Recorder error.");
    }
}
