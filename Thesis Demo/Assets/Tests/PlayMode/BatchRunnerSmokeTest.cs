using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// End-to-end smoke test for ExperimentRunner batch mode. Configures a minimal
/// batch (1 condition × 1 run × 2s, both Combat + Stress modes), runs it, and
/// asserts the TrialSummary.csv landed with the expected header + row count.
/// Confirms the termination / metrics / batch pipeline is wired correctly after
/// the methodology-revisions-2026-04 stress-mode changes (items 1 / 3b).
/// </summary>
[TestFixture]
public class BatchRunnerSmokeTest
{
    private const string SceneName = "New Scene";

    [UnityTest]
    public IEnumerator Batch_DualMode_PureBOIDS_WritesTrialSummaryWithBenchmarkMode()
    {
        if (!Application.CanStreamedLevelBeLoaded(SceneName))
        {
            Assert.Inconclusive(
                $"Scene '{SceneName}' is not in Build Settings. " +
                "Add 'Assets/Scenes/New Scene.unity' under File → Build Profiles → Scene List.");
            yield break;
        }

        var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
        while (!load.isDone) yield return null;
        yield return null;

        var cm = Object.FindFirstObjectByType<ConditionManager>();
        if (cm == null) { Assert.Inconclusive("No ConditionManager in scene."); yield break; }

        var runner = cm.GetComponent<ExperimentRunner>();
        if (runner == null) { Assert.Inconclusive("No ExperimentRunner on ConditionManager GameObject."); yield break; }

        // Unique temp dir so the test doesn't collide with real batches.
        string tempDir = Path.Combine(Path.GetTempPath(), $"BatchSmoke_{System.DateTime.Now:HHmmss_fff}");
        Directory.CreateDirectory(tempDir);

        SetField(runner, "runsPerCondition", 1);
        SetField(runner, "experimentDuration", 2f);
        SetField(runner, "transitionDelay", 0.5f);
        SetField(runner, "outputDirectory", tempDir);
        SetField(runner, "baseSeed", 42);
        SetField(runner, "conditionsToTest", new[] { AgentCondition.PureBOIDS });
        // Override the Inspector size sweep — the scene may have a multi-size sweep
        // configured for real batches; the smoke test wants a single small size.
        SetField(runner, "agentCountsToTest", new[] { 25 });
        // Skip the stimulus warmup — the smoke test is verifying CSV plumbing, not
        // reaction-time correctness, and the 5s default would triple the test time.
        SetField(runner, "stimulusWarmupSec", 0f);
        // Run both modes so the test exercises the stress path too.
        SetField(runner, "modesToTest", new[] { BenchmarkMode.Combat, BenchmarkMode.Stress });

        runner.RunAllExperiments();

        // 2 modes × 1 size × 1 run × 1 condition = 2 trials. Allow 30s wall time
        // (2s trial + 0.5s delay + ~0.5s spawn = ~3s per trial; doubled for safety).
        float wallDeadline = Time.realtimeSinceStartup + 30f;
        while ((bool)GetField(runner, "isRunning"))
        {
            if (Time.realtimeSinceStartup > wallDeadline)
                Assert.Fail("Batch did not complete within 30s wall time.");
            yield return null;
        }

        // Verify outputs.
        string[] summaries = Directory.GetFiles(tempDir, "TrialSummary_batch_*.csv");
        Assert.AreEqual(1, summaries.Length, "Expected exactly one TrialSummary CSV.");

        string[] lines = File.ReadAllLines(summaries[0]);
        Assert.AreEqual(3, lines.Length, "Expected header + 2 data rows (Combat + Stress).");

        string header = lines[0];
        StringAssert.Contains("BenchmarkMode", header);
        StringAssert.Contains("Condition", header);
        StringAssert.Contains("RunId", header);
        StringAssert.Contains("Seed", header);
        StringAssert.Contains("Outcome", header);
        StringAssert.Contains("DurationSec", header);
        StringAssert.Contains("ReactionMeleeMs", header);
        StringAssert.Contains("GoalEntropy", header);
        StringAssert.Contains("AvgCpuMsPerAgent", header);
        // Combat-effectiveness metrics (methodology-revisions item 2).
        StringAssert.Contains("TotalDamageToPlayer", header);
        StringAssert.Contains("DamagePerSecondToPlayer", header);
        StringAssert.Contains("FirstHitMs", header);
        StringAssert.Contains("AgentsKilledByPlayer", header);

        // Each mode appears as the first column of one row.
        string allRows = string.Join("\n", lines);
        StringAssert.Contains("Combat,PureBOIDS", allRows);
        StringAssert.Contains("Stress,PureBOIDS", allRows);

        // Sibling CSVs should also exist with the BenchmarkMode column.
        Assert.AreEqual(1, Directory.GetFiles(tempDir, "Performance_batch_*.csv").Length);
        Assert.AreEqual(1, Directory.GetFiles(tempDir, "GoalDistribution_batch_*.csv").Length);
        Assert.AreEqual(1, Directory.GetFiles(tempDir, "EventLog_batch_*.csv").Length);

        string perfHeader = File.ReadAllLines(Directory.GetFiles(tempDir, "Performance_batch_*.csv")[0])[0];
        StringAssert.Contains("BenchmarkMode", perfHeader);
        string goalsHeader = File.ReadAllLines(Directory.GetFiles(tempDir, "GoalDistribution_batch_*.csv")[0])[0];
        StringAssert.Contains("BenchmarkMode", goalsHeader);
        string eventsHeader = File.ReadAllLines(Directory.GetFiles(tempDir, "EventLog_batch_*.csv")[0])[0];
        StringAssert.Contains("BenchmarkMode", eventsHeader);
    }

    // ── Reflection helpers (test-only) ──

    private static void SetField(object target, string name, object value)
    {
        var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null) throw new System.Exception($"No field '{name}' on {target.GetType().Name}");
        f.SetValue(target, value);
    }

    private static object GetField(object target, string name)
    {
        var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null) throw new System.Exception($"No field '{name}' on {target.GetType().Name}");
        return f.GetValue(target);
    }
}
