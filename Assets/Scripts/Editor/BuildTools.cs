#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GVGame.EditorTools
{
    /// <summary>
    /// Player builds:  menu  GV Game > Build > Windows
    /// Builds the enabled scenes of the build list (Main.unity first) to a folder outside the repo.
    /// From the command line:
    ///   Unity.exe -batchmode -quit -projectPath &lt;project&gt; -executeMethod GVGame.EditorTools.BuildTools.BuildWindows -buildPath &lt;folder&gt; -logFile build.log
    /// Without -buildPath the build goes to ../GV-Game-Build/Windows next to the repo.
    /// Throws (exit code 1) when the build fails.
    /// </summary>
    public static class BuildTools
    {
        const string ExeName = "LabLockdown.exe";

        [MenuItem("GV Game/Build/Windows")]
        public static void BuildWindows()
        {
            string folder = ArgValue("-buildPath")
                ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "GV-Game-Build", "Windows"));
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("[Build] No enabled scenes in File > Build Profiles.");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(folder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"[Build] {summary.result}: {summary.totalErrors} errors. See the log.");
            Debug.Log($"[Build] {options.locationPathName} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F0} s)");
        }

        static string ArgValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
#endif
