using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebGLBuildAutomation
{
    const string BuildDirectory = "Builds/WebGL";
    const string BuildPath = BuildDirectory;

    [MenuItem("Tools/Portfolio/Build WebGL Showcase")]
    public static void BuildShowcase()
    {
        Directory.CreateDirectory(BuildDirectory);

        string[] scenes =
        {
            "Assets/Scenes/IntroMenu.unity",
            "Assets/Scenes/MainScene.unity"
        };

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = BuildPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.CleanBuildCache
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            throw new BuildFailedException(
                $"WebGL build failed with {summary.totalErrors} errors and {summary.totalWarnings} warnings.");
        }

        Debug.Log(
            $"[WebGL Build] Complete: {BuildPath} " +
            $"({summary.totalSize / (1024f * 1024f):0.0} MB, {summary.totalTime}).");
    }
}
