using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the Windows player of the session into Temp/PlayerBuild; builds.ps1 then snapshots it into
/// builds/. From the menu (Tools/Build/Windows Player) or the command line:
/// Unity.exe -batchmode -quit -projectPath . -executeMethod PlayerBuild.BuildWindows -logFile -
/// </summary>
public static class PlayerBuild
{
    public const string OutputFolder = "Temp/PlayerBuild";
    const string SessionScene = "Assets/Session/oak_session.unity";

    [MenuItem("Tools/Build/Windows Player")]
    public static void BuildWindows()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { SessionScene }, // the session is the whole app
            locationPathName = $"{OutputFolder}/{PlayerSettings.productName}.exe",
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        };
        if (System.IO.Directory.Exists(OutputFolder)) FileUtil.DeleteFileOrDirectory(OutputFolder);

        var report = BuildPipeline.BuildPlayer(options);
        var s = report.summary;
        Debug.Log($"[Build] {s.result}: build {BuildInfo.Number}, {s.totalSize / (1024f * 1024f):0} MB, " +
                  $"{s.totalErrors} errors, {s.totalTime.TotalSeconds:0} s → {s.outputPath}");
        if (s.result != BuildResult.Succeeded)
        {
            foreach (var m in report.steps.SelectMany(st => st.messages).Where(m => m.type == LogType.Error))
                Debug.LogError($"[Build] {m.content}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
