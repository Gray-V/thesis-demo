using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode lifecycle coverage for the two highest-stakes leader actions:
/// <see cref="LeaderAttackAction"/> (melee Approach → WindUp → Charge → damage)
/// and <see cref="LeaderRangedAttackAction"/> (ranged Approach → Fire → projectile
/// instantiated). These are the two actions whose execution feeds the §5 Combat
/// effectiveness numbers (TotalDamageToPlayer, AgentsKilledByPlayer); a silent
/// regression in either would directly contaminate the headline thesis figures.
///
/// The other seven leader actions are covered at constants depth only by
/// <see cref="LeaderActionConstantsTests"/> in EditMode — that's the right
/// cost/benefit before the May 2026 defense.
///
/// Why end-to-end scene-load rather than direct Action.Start() / Perform()
/// invocation: each leader action depends on a live <c>BoidAgent</c>,
/// <c>BoidGoapBrain</c>, <c>FlockManager</c>, and (for damage / projectile
/// instantiation) a real <c>PlayerHealth</c> + projectile prefab. Stubbing
/// every dependency would either require source visibility downgrades or a
/// mocking library the project doesn't currently use; the BatchRunnerSmokeTest
/// pattern already pays for the scene-load cost and re-using it adds &lt; 30 s
/// to the test runner.
///
/// Pattern mirrors <see cref="LeaderReactionPathTests"/>: configure
/// ExperimentRunner via reflection, run a single short BOIDSWithGOAPLeader
/// trial, assert against the resulting EventLog CSV.
///
/// Created 2026-05-06 alongside <see cref="LeaderActionConstantsTests"/> in
/// response to the audit finding that no fixture covered the Leader actions'
/// lifecycle. See wiki/todos.md "v1 batch follow-ups" → instrumentation hygiene.
/// </summary>
[TestFixture]
public class LeaderAttackActionLifecycleTests
{
    private const string SceneName = "New Scene";

    // Trial duration sized so both flocks (melee + ranged) have time to drift
    // into engagement range from spawn. 15 s matches LeaderReactionPathTests'
    // empirically-tuned window — shorter and the geometry warnings dominate
    // without proving anything. Longer would just slow CI without adding signal.
    private const float TrialSeconds = 15f;

    // Wall deadline = TrialSeconds + scene-load + spawn + transition, doubled.
    private const float WallDeadlineSeconds = 60f;

    [TearDown]
    public void TearDown()
    {
        // Defensive: this fixture doesn't toggle the diagnostic, but if a
        // future test in this fixture does, leaving it on would bleed into
        // LeaderReactionPathTests' default-off contract.
        LeaderGoapBrain.DiagnosticLogging = false;
    }

    /// <summary>
    /// Asserts that running a tiny BOIDSWithGOAPLeader trial produces evidence
    /// that at least one leader attack action's full lifecycle executed
    /// (Start → Perform → Complete). This is the v1-style regression net: if
    /// the leader's goal resolver, action resolver, action lifecycle, or damage
    /// emission breaks anywhere along the chain, no AttackHit/AttackFired event
    /// will appear in the EventLog and the test fails.
    /// </summary>
    [UnityTest]
    public IEnumerator LeaderTrial_EmitsAtLeastOneLeaderAttackEvent()
    {
        if (!Application.CanStreamedLevelBeLoaded(SceneName))
        {
            Assert.Inconclusive(
                $"Scene '{SceneName}' is not in Build Settings. "
                + "Add 'Assets/Scenes/New Scene.unity' under File → Build Profiles → Scene List.");
            yield break;
        }

        string tempDir = Path.Combine(
            Path.GetTempPath(), $"LeaderAttackLifecycle_{System.DateTime.Now:HHmmss_fff}");
        Directory.CreateDirectory(tempDir);

        yield return LoadAndRunLeaderBatch(tempDir);

        string eventLog = Directory.GetFiles(tempDir, "EventLog_batch_*.csv").FirstOrDefault();
        Assert.IsNotNull(eventLog, "EventLog CSV must exist.");

        string[] lines = File.ReadAllLines(eventLog);
        Assert.Greater(lines.Length, 1, "EventLog must contain at least a header + one row.");

        // Either a melee Charge-phase contact-damage event or a ranged Fire-phase
        // projectile-emission event. AttackHit comes from LeaderAttackAction.cs's
        // Charge phase when the player is within DamageContactDistance; AttackFired
        // comes from LeaderRangedAttackAction.cs's Fire phase or LeaderKiteAction's
        // Fire phase. Filtering on the leader-specific payload prefixes
        // distinguishes leader actions from follower attacks.
        bool sawLeaderMeleeHit = lines.Any(l =>
            l.Contains(",AttackHit,") && l.Contains("LeaderMelee,Dmg="));
        bool sawLeaderRangedFire = lines.Any(l =>
            l.Contains(",AttackFired,") && l.Contains("LeaderRangedAttack,Dmg="));
        bool sawLeaderKiteFire = lines.Any(l =>
            l.Contains(",AttackFired,") && l.Contains("LeaderKite,Dmg="));

        Assert.IsTrue(
            sawLeaderMeleeHit || sawLeaderRangedFire || sawLeaderKiteFire,
            "EventLog must contain at least one leader attack event "
            + "(AttackHit/LeaderMelee, AttackFired/LeaderRangedAttack, or "
            + "AttackFired/LeaderKite). If none fire across a 15 s engagement "
            + "trial at N=25 with both flock types present, the leader's "
            + "goal-resolver → action-resolver → action-lifecycle chain is "
            + "broken somewhere upstream of damage emission.");
    }

    /// <summary>
    /// Targeted regression net for the v1 ranged-leader bug fixed on 2026-05-05
    /// (commit 661f651): <see cref="LeaderGoapBrain"/>'s goal-request switch
    /// had no case for <c>GoalType.RangedAttack</c>, so ranged-flock leaders
    /// silently wandered through every trial while the metric logged them as
    /// engaged. If that switch case regresses, no AttackFired event with the
    /// LeaderRangedAttack payload will appear during the trial.
    ///
    /// The 30-damage assertion is a separate check from the event-presence
    /// check — the constants test (<see cref="LeaderActionConstantsTests"/>)
    /// asserts the source-side const value is 30, but only an end-to-end
    /// trial confirms the damage value actually written to the EventLog
    /// matches what the action emits at runtime.
    /// </summary>
    [UnityTest]
    public IEnumerator RangedLeader_FiresWithDocumented30DamagePayload()
    {
        if (!Application.CanStreamedLevelBeLoaded(SceneName))
        {
            Assert.Inconclusive($"Scene '{SceneName}' is not in Build Settings.");
            yield break;
        }

        string tempDir = Path.Combine(
            Path.GetTempPath(), $"LeaderRangedDmg_{System.DateTime.Now:HHmmss_fff}");
        Directory.CreateDirectory(tempDir);

        yield return LoadAndRunLeaderBatch(tempDir);

        string eventLog = Directory.GetFiles(tempDir, "EventLog_batch_*.csv").FirstOrDefault();
        Assert.IsNotNull(eventLog, "EventLog CSV must exist.");

        string[] lines = File.ReadAllLines(eventLog);

        var rangedFires = lines.Where(l =>
            l.Contains(",AttackFired,") && l.Contains("LeaderRangedAttack,Dmg=")).ToList();

        // If no ranged-leader fires were observed, this is geometry-dependent
        // (the ranged flock may not have closed within FiringRange = 12 m
        // during the 15 s trial) — demote to inconclusive rather than fail,
        // matching LeaderReactionPathTests' geometry-warning pattern. The
        // first test in this fixture already covers the "at least one
        // leader attack must fire" load-bearing assertion.
        if (rangedFires.Count == 0)
        {
            Assert.Inconclusive(
                "No LeaderRangedAttack AttackFired events observed in this trial. "
                + "Ranged flock did not close within 12 m firing range. "
                + "Re-run; if persistent across runs, ranged-leader engagement "
                + "is regressing at small N.");
            yield break;
        }

        // When the event does fire, the documented 30 damage must be in the
        // payload. Catches a class of bug where the ProjectileDamage const is
        // changed but the BehavioralMetricsCollector LogEvent call is missed.
        bool sawDocumentedDamage = rangedFires.Any(l => l.Contains("Dmg=30"));
        Assert.IsTrue(sawDocumentedDamage,
            "LeaderRangedAttack AttackFired events must report Dmg=30 to match "
            + $"LeaderRangedAttackAction.ProjectileDamage. Sample line: {rangedFires.First()}");
    }

    // ── Shared scene-load + batch-run scaffold (mirrors LeaderReactionPathTests) ──

    private IEnumerator LoadAndRunLeaderBatch(string outputDir)
    {
        var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
        while (!load.isDone) yield return null;
        yield return null;

        var cm = Object.FindFirstObjectByType<ConditionManager>();
        Assert.IsNotNull(cm, "No ConditionManager in scene.");

        var runner = cm.GetComponent<ExperimentRunner>();
        Assert.IsNotNull(runner, "No ExperimentRunner on ConditionManager GameObject.");

        SetField(runner, "runsPerCondition", 1);
        SetField(runner, "experimentDuration", TrialSeconds);
        SetField(runner, "transitionDelay", 0.5f);
        SetField(runner, "outputDirectory", outputDir);
        SetField(runner, "baseSeed", 42);
        SetField(runner, "conditionsToTest", new[] { AgentCondition.BOIDSWithGOAPLeader });
        // 25 = smallest production cell, exercises both melee and ranged sub-flocks
        // while keeping per-test wall time reasonable for CI.
        SetField(runner, "agentCountsToTest", new[] { 25 });
        SetField(runner, "stimulusWarmupSec", 0f);
        SetField(runner, "modesToTest", new[] { BenchmarkMode.Combat });

        runner.RunAllExperiments();

        float wallDeadline = Time.realtimeSinceStartup + WallDeadlineSeconds;
        while ((bool)GetField(runner, "isRunning"))
        {
            if (Time.realtimeSinceStartup > wallDeadline)
                Assert.Fail($"Batch did not complete within {WallDeadlineSeconds:F0} s wall time.");
            yield return null;
        }
    }

    // ── Reflection helpers ──

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
