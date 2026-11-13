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
        const string version = "0.5.4";
        const string output = "Builds/0.5.4/Windows-x64/Dicebound.exe";
        PlayerSettings.bundleVersion = version;
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Branding/dicebound-icon.png");
        if (icon == null) throw new Exception("Dicebound application icon is missing.");
        foreach (var target in new[] { UnityEditor.Build.NamedBuildTarget.Unknown, UnityEditor.Build.NamedBuildTarget.Standalone })
        {
            int count = PlayerSettings.GetIconSizes(target, IconKind.Any).Length;
            PlayerSettings.SetIcons(target, Enumerable.Repeat(icon, Mathf.Max(1, count)).ToArray(), IconKind.Any);
        }
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
        File.WriteAllText("Logs/release-0.5.4-build-result.json", JsonUtility.ToJson(new Result
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
