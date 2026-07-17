using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FPS.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ProjectValidationRunner
{
    const string ReportPath = "Logs/editor-validation-report.txt";

    [MenuItem("Tools/Validation/Run Project Validation")]
    public static void RunValidation()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[Validation] Cancelled.");
            return;
        }

        string originalScenePath = SceneManager.GetActiveScene().path;
        var report = new StringBuilder();
        var issues = new List<string>();

        report.AppendLine("Project Validation Report");
        report.AppendLine($"Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine();

        ValidateBuildSettings(report, issues);
        ValidateScene("Assets/Scenes/IntroMenu.unity", report, issues, ValidateIntroMenuScene);
        ValidateScene("Assets/Scenes/MainScene.unity", report, issues, ValidateMainScene);

        report.AppendLine();
        report.AppendLine("Summary");
        if (issues.Count == 0)
        {
            report.AppendLine("- No validation issues found.");
        }
        else
        {
            foreach (string issue in issues)
            {
                report.AppendLine($"- {issue}");
            }
        }

        string fullReportPath = Path.GetFullPath(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath));
        File.WriteAllText(fullReportPath, report.ToString());

        if (!string.IsNullOrEmpty(originalScenePath) && File.Exists(originalScenePath))
        {
            EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
        }

        Debug.Log($"[Validation] Report written to {fullReportPath}");
        if (issues.Count > 0)
        {
            Debug.LogWarning($"[Validation] Completed with {issues.Count} issue(s). See {fullReportPath}");
        }
    }

    static void ValidateBuildSettings(StringBuilder report, List<string> issues)
    {
        report.AppendLine("Build Settings");

        bool hasIntroMenu = false;
        bool hasMainScene = false;

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            report.AppendLine($"- {(scene.enabled ? "[Enabled]" : "[Disabled]")} {scene.path}");

            if (scene.path == "Assets/Scenes/IntroMenu.unity" && scene.enabled) hasIntroMenu = true;
            if (scene.path == "Assets/Scenes/MainScene.unity" && scene.enabled) hasMainScene = true;
        }

        if (!hasIntroMenu) AddIssue("IntroMenu is not enabled in Build Settings.", report, issues);
        if (!hasMainScene) AddIssue("MainScene is not enabled in Build Settings.", report, issues);

        report.AppendLine();
    }

    static void ValidateScene(
        string scenePath,
        StringBuilder report,
        List<string> issues,
        System.Action<Scene, StringBuilder, List<string>> validator)
    {
        report.AppendLine(scenePath);

        if (!File.Exists(scenePath))
        {
            AddIssue($"Scene is missing: {scenePath}", report, issues);
            report.AppendLine();
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        validator(scene, report, issues);
        report.AppendLine();
    }

    static void ValidateIntroMenuScene(Scene scene, StringBuilder report, List<string> issues)
    {
        CheckNamedComponentInScene(scene, "NetworkManager", report, issues);
        CheckNamedComponentInScene(scene, "LobbyManager", report, issues);
        CheckNamedComponentInScene(scene, "RelayManager", report, issues);
        CheckNamedComponentInScene(scene, "ServicesInitializer", report, issues);
        CheckNamedComponentInScene(scene, "MenuUI", report, issues);
    }

    static void ValidateMainScene(Scene scene, StringBuilder report, List<string> issues)
    {
        CheckNamedComponentInScene(scene, "GameFlowManager", report, issues);
        CheckNamedComponentInScene(scene, "NetworkManager", report, issues);

        int spawnPointCount = CountInScene<PlayerSpawnPoint>(scene);
        report.AppendLine($"- PlayerSpawnPoint count: {spawnPointCount}");
        if (spawnPointCount < 2)
        {
            AddIssue("MainScene should have at least two PlayerSpawnPoint objects for safe respawns.", report, issues);
        }

        CheckNamedComponentInScene(scene, "ScoreboardUI", report, issues);
        CheckNamedComponentInScene(scene, "PlayerSpawner", report, issues);
        CheckNamedComponentInScene(scene, "NetworkPlayerSpawner", report, issues);

        int pickupCount = CountNamedComponentInScene(scene, "Pickup");
        report.AppendLine($"- Pickup-derived component count: {pickupCount}");
        if (pickupCount == 0)
        {
            AddIssue("MainScene has no pickup components.", report, issues);
        }
    }

    static void CheckSingletonInScene<T>(
        Scene scene,
        string label,
        StringBuilder report,
        List<string> issues) where T : Object
    {
        int count = CountInScene<T>(scene);
        report.AppendLine($"- {label} count: {count}");

        if (count == 0)
        {
            AddIssue($"{scene.path} is missing {label}.", report, issues);
        }
        else if (count > 1)
        {
            AddIssue($"{scene.path} has multiple {label} instances.", report, issues);
        }
    }

    static void CheckNamedComponentInScene(
        Scene scene,
        string componentName,
        StringBuilder report,
        List<string> issues)
    {
        int count = CountNamedComponentInScene(scene, componentName);
        report.AppendLine($"- {componentName} count: {count}");

        if (count == 0)
        {
            AddIssue($"{scene.path} is missing {componentName}.", report, issues);
        }
        else if (count > 1)
        {
            AddIssue($"{scene.path} has multiple {componentName} instances.", report, issues);
        }
    }

    static int CountInScene<T>(Scene scene) where T : Object
    {
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            count += root.GetComponentsInChildren<T>(true).Length;
        }

        return count;
    }

    static int CountNamedComponentInScene(Scene scene, string componentName)
    {
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;

                string typeName = behaviour.GetType().Name;
                bool matches = typeName == componentName ||
                    (componentName == "Pickup" && typeName.EndsWith(componentName));

                if (matches)
                {
                    count++;
                }
            }
        }

        return count;
    }

    static void AddIssue(string issue, StringBuilder report, List<string> issues)
    {
        issues.Add(issue);
        report.AppendLine($"  ISSUE: {issue}");
    }
}
