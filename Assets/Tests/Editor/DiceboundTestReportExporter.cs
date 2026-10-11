using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Keep NUnit XML export registered across EnterPlayMode domain reloads.</summary>
[InitializeOnLoad]
internal static class DiceboundTestReportExporter
{
    static DiceboundTestReportExporter() => EditorApplication.delayCall += Register;

    private static void Register()
    {
        string request = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "test-report-path.txt");
        if (!File.Exists(request)) return;
        string destination = File.ReadAllText(request).Trim();
        if (string.IsNullOrEmpty(destination)) return;
        string projectRoot = Path.GetFullPath(Directory.GetCurrentDirectory()) + Path.DirectorySeparatorChar;
        destination = Path.GetFullPath(destination);
        if (!destination.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)) return;
        var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a =>
            a.GetType("UnityEditor.TestTools.TestRunner.Api.TestRunnerApi") != null);
        var callbackType = assembly.GetType("UnityEditor.TestTools.TestRunner.CommandLineTest.ResultsSavingCallbacks", true);
        var callback = Resources.FindObjectsOfTypeAll(callbackType).FirstOrDefault() as ScriptableObject
            ?? ScriptableObject.CreateInstance(callbackType);
        callback.hideFlags = HideFlags.HideAndDontSave;
        callbackType.GetField("m_ResultFilePath").SetValue(callback, destination);
        assembly.GetType("UnityEditor.TestTools.TestRunner.Api.TestRunnerApi", true)
            .GetMethod("RegisterTestCallback", BindingFlags.Public | BindingFlags.Static)
            .MakeGenericMethod(callbackType).Invoke(null, new object[] { callback, 0 });
    }
}
