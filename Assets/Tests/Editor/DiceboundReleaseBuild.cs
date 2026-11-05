using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class DiceboundReleaseBuild
{
    public static void BuildWindows()
    {
        const string version = "0.5.0";
        const string output = "Builds/0.5.0/Windows-x64/Dicebound.exe";
        PlayerSettings.bundleVersion = version;
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        var summary = report.summary;
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/release-0.5.0-build-result.json", JsonUtility.ToJson(new Result
        {
            version = version, result = summary.result.ToString(),
            errors = summary.totalErrors, warnings = summary.totalWarnings,
            bytes = summary.totalSize, seconds = summary.totalTime.TotalSeconds,
            output = Path.GetFullPath(output)
        }, true));
        if (summary.result != BuildResult.Succeeded)
            throw new Exception("Release build failed: " + summary.result);
    }

    [Serializable]
    private class Result
    {
        public string version, result, output;
        public int errors, warnings;
        public ulong bytes;
        public double seconds;
    }
}
