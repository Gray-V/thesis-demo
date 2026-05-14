using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor hooks for triggering the batch experiment runner.
///
/// Two entry points:
///   1. <b>Menu item</b> — "Thesis → Run Batch Experiment" — opens the target scene,
///      enters PlayMode, and kicks off <see cref="ExperimentRunner.RunAllExperiments"/>
///      once play starts. Use this from the open Editor.
///   2. <b>CLI -executeMethod</b> — <see cref="RunBatchFromCommandLine"/> is callable
///      from a closed-Unity terminal invocation. See the header comment in this file
///      for the exact command.
///
/// Both paths rely on the same scene ("Assets/Scenes/New Scene.unity") and the scene
/// containing a ConditionManager + ExperimentRunner + BehavioralMetricsCollector +
/// PerformanceProfiler. Output CSVs land on the user's Desktop by default
/// (configurable on the ExperimentRunner component).
///
/// CLI invocation (run in PowerShell after CLOSING Unity):
///   &amp; "C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" `
///      -projectPath "C:\Users\cliff\Desktop\Thesis\AI_Game_demo\My project" `
///      -executeMethod ExperimentBatchCli.RunBatchFromCommandLine `
///      -logFile "$env:USERPROFILE\Desktop\unity-batch.log"
///
/// The -executeMethod path keeps the Editor open after kicking off the batch so
/// PlayMode can actually run (full -batchmode disables PlayMode). Close Unity
/// manually when the batch is done (the Debug.Log summary will tell you when).
/// </summary>
public static class ExperimentBatchCli
{
    private const string TargetScenePath = "Assets/Scenes/New Scene.unity";

    [MenuItem("Thesis/Run Batch Experiment")]
    public static void RunBatchFromMenu()
    {
        OpenSceneIfNeeded();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (!EditorApplication.isPlaying)
            EditorApplication.EnterPlaymode();
        else
            TryStartBatch(); // already in play — just kick it
    }

    /// <summary>
    /// Called by Unity when invoked with -executeMethod ExperimentBatchCli.RunBatchFromCommandLine.
    /// Opens the scene, enters PlayMode, then yields the batch.
    /// </summary>
    public static void RunBatchFromCommandLine()
    {
        OpenSceneIfNeeded();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    private static void OpenSceneIfNeeded()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.path != TargetScenePath)
        {
            if (!File.Exists(TargetScenePath))
            {
                Debug.LogError($"[ExperimentBatchCli] Scene not found: {TargetScenePath}");
                return;
            }
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            EditorSceneManager.OpenScene(TargetScenePath);
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            TryStartBatch();
        }
    }

    private static void TryStartBatch()
    {
        var runner = Object.FindFirstObjectByType<ExperimentRunner>();
        if (runner == null)
        {
            Debug.LogError("[ExperimentBatchCli] No ExperimentRunner in the active scene.");
            return;
        }
        runner.RunAllExperiments();
        Debug.Log("[ExperimentBatchCli] Batch started. Watch the Console for per-trial progress. " +
                  "Final summary will also log there and CSVs land on Desktop.");
    }

    [MenuItem("Thesis/Stop Batch")]
    public static void StopBatch()
    {
        if (EditorApplication.isPlaying)
            EditorApplication.ExitPlaymode();
    }
}
