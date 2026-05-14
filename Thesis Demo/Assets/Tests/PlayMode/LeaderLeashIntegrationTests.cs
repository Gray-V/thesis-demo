using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode integration test for the leader↔follower coupling in the
/// BOIDSWithGOAPLeader condition. The fixture catches the integration failure
/// mode CJ observed while watching a live trial — leaders drifting away from
/// the flock and the flock not tracking, producing visually disjointed swarms.
///
/// Coupling model (revised 2026-05-06): the leader-side velocity damping was
/// removed; followers now run standard BOIDS cohesion plus a light additive
/// bias toward the leader (<c>FlockManager.LateUpdate</c>'s
/// <c>leaderInfluenceWeight = 0.3f</c>). The flock self-organizes around the
/// leader's general trajectory rather than the leader being constrained.
///
/// What this fixture validates: the bias is strong enough to keep the
/// follower bulk within a flock-shaped envelope of the leader. The bound is
/// behavior-correct rather than formula-derived — a flock-radius value at
/// which the swarm still reads as "a flock with a leader" rather than "two
/// disjoint groups." If the leader drifts far past the bound the bias is too
/// weak; if the test bound becomes hard to hit even at N=200 the bias is too
/// strong and we should pull it down.
///
/// The test samples leader-vs-follower-only-centroid every ~0.5 s during a
/// short Combat trial and asserts the max sustained gap stays under the
/// configured envelope. If it fails, the diagnostic message reports per-flock
/// max gap so we can decide whether to tune <c>leaderInfluenceWeight</c>.
/// </summary>
[TestFixture]
public class LeaderLeashIntegrationTests
{
    private const string SceneName = "New Scene";
    private const float SampleIntervalSeconds = 0.5f;
    // Behavior-correct envelope for the new follower-bias coupling model
    // (2026-05-06 redesign). Room radius is 50 m; a 25 m envelope means the
    // leader and the follower bulk are at most half the room apart, which
    // still reads as a single flock. If the bias proves too weak to hit this
    // bound at N=50 / N=200, the right move is to raise leaderInfluenceWeight
    // in FlockManager.LateUpdate (currently 0.3f) before tightening this
    // constant.
    private const float MaxAllowedSeparation = 25f;

    [UnityTest]
    public IEnumerator Leader_StaysBoundedToFollowers_AtN50_15s()
    {
        // Smallest cell that still produces meaningful follower-centroid gaps
        // (~30 melee + 20 ranged after the standard 60/40 split). Cheap enough
        // to run on every CI cycle without dominating the PlayMode suite.
        yield return RunLeashTrialAndAssert(agentCount: 50, trialSeconds: 15f);
    }

    [UnityTest]
    public IEnumerator Leader_StaysBoundedToFollowers_AtN200_30s()
    {
        // The N CJ observed runaway leaders at live (post-v1 batch). Larger
        // flock + longer window exercises sustained leash behaviour over
        // multiple GOAP-action cycles (Attack/Flank/Kite/Wander rotations
        // through the full ranged-leader engagement loop), where centroid
        // dilution and execution-order bugs are most likely to surface.
        // Wall budget ≈ 30 s trial + 0.5 s transition + ~8 s spawn at N=200,
        // doubled for safety = ~80 s; the helper sizes the deadline.
        yield return RunLeashTrialAndAssert(agentCount: 200, trialSeconds: 30f);
    }

    private IEnumerator RunLeashTrialAndAssert(int agentCount, float trialSeconds)
    {
        if (!Application.CanStreamedLevelBeLoaded(SceneName))
        {
            Assert.Inconclusive(
                $"Scene '{SceneName}' is not in Build Settings. "
                + "Add 'Assets/Scenes/New Scene.unity' under File → Build Profiles → Scene List.");
            yield break;
        }

        var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
        while (!load.isDone) yield return null;
        yield return null;

        var cm = Object.FindFirstObjectByType<ConditionManager>();
        Assert.IsNotNull(cm, "No ConditionManager in scene.");
        var runner = cm.GetComponent<ExperimentRunner>();
        Assert.IsNotNull(runner, "No ExperimentRunner.");

        string tempDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"LeashIntegration_N{agentCount}_{System.DateTime.Now:HHmmss_fff}");
        System.IO.Directory.CreateDirectory(tempDir);

        SetField(runner, "runsPerCondition", 1);
        SetField(runner, "experimentDuration", trialSeconds);
        SetField(runner, "transitionDelay", 0.5f);
        SetField(runner, "outputDirectory", tempDir);
        SetField(runner, "baseSeed", 42);
        SetField(runner, "conditionsToTest", new[] { AgentCondition.BOIDSWithGOAPLeader });
        SetField(runner, "agentCountsToTest", new[] { agentCount });
        SetField(runner, "stimulusWarmupSec", 0f);
        SetField(runner, "modesToTest", new[] { BenchmarkMode.Combat });

        runner.RunAllExperiments();

        // Per-flock max-gap tracker — keyed by flock GameObject name so melee
        // and ranged are reported separately in the failure message.
        var maxGapByFlock = new Dictionary<string, float>();
        var sampleCountByFlock = new Dictionary<string, int>();
        var lastBreachByFlock = new Dictionary<string, float>();
        float lastSampleTime = -SampleIntervalSeconds;

        // Wall deadline scales with trial duration plus generous spawn +
        // transition overhead (≈ 10 s for spawn at N=200, plus the 0.5 s
        // transition plus a 2× safety factor on the trial itself).
        float wallDeadline = Time.realtimeSinceStartup + (trialSeconds * 2f + 20f);
        while ((bool)GetField(runner, "isRunning"))
        {
            if (Time.realtimeSinceStartup > wallDeadline)
                Assert.Fail($"Trial at N={agentCount} did not complete within wall deadline.");

            if (Time.time - lastSampleTime >= SampleIntervalSeconds)
            {
                lastSampleTime = Time.time;
                SampleAllFlocks(maxGapByFlock, sampleCountByFlock, lastBreachByFlock);
            }

            yield return null;
        }

        // Report per-flock results. We only assert when we actually got
        // samples (a flock with no leader produces no samples and that's
        // a separate concern, not a leash failure).
        Assert.Greater(maxGapByFlock.Count, 0,
            $"Test at N={agentCount} did not observe any flock with a leader. "
            + "Either spawning failed or the BOIDSWithGOAPLeader condition is "
            + "not assigning leaders to flocks.");

        var failures = new List<string>();
        foreach (var kv in maxGapByFlock)
        {
            int samples = sampleCountByFlock[kv.Key];
            float lastBreach = lastBreachByFlock.TryGetValue(kv.Key, out float v) ? v : -1f;
            if (kv.Value > MaxAllowedSeparation)
            {
                failures.Add(
                    $"  • {kv.Key}: max leader-vs-follower-centroid gap = {kv.Value:F1} m "
                    + $"(allowed: {MaxAllowedSeparation:F0} m), {samples} samples, "
                    + $"last breach at t={lastBreach:F1} s");
            }
        }

        if (failures.Count > 0)
        {
            string summary = string.Join("\n", failures);
            Assert.Fail(
                $"Leader leash failed at N={agentCount} (trial = {trialSeconds:F0} s).\n"
                + summary
                + "\n\nLikely causes (in priority order):\n"
                + "  1. Script execution order — GOAP Action.Perform runs after\n"
                + "     LeaderGoapBrain.Update, so the leash velocity multiplier\n"
                + "     is overwritten before BoidAgent integrates velocity.\n"
                + "  2. GetFlockCenter() includes the leader's own position,\n"
                + "     diluting the leash distance metric at small flock counts.\n"
                + "  3. LeaderKiteAction.Phase.Retreat overshoots kite range and\n"
                + "     keeps backpedaling for the full 8 s KiteTimer.");
        }
    }

    /// <summary>
    /// Walks every active <see cref="FlockManager"/> in the scene, computes the
    /// leader's distance to the *follower-only* centroid (excluding the leader
    /// itself, unlike <see cref="FlockManager.GetFlockCenter"/>), and updates
    /// the per-flock max + last-breach trackers. Skips flocks without a leader
    /// or with no followers.
    /// </summary>
    private static void SampleAllFlocks(
        Dictionary<string, float> maxGapByFlock,
        Dictionary<string, int> sampleCountByFlock,
        Dictionary<string, float> lastBreachByFlock)
    {
        FlockManager[] flocks = Object.FindObjectsByType<FlockManager>(FindObjectsSortMode.None);
        foreach (var fm in flocks)
        {
            BoidAgent leader = fm.LeaderBoid;
            if (leader == null) continue;
            if (fm.BoidCount <= 1) continue; // leader alone — no follower centroid

            Vector3 followerCentroid = ComputeFollowerCentroid(fm, leader);
            float gap = Vector3.Distance(leader.Position, followerCentroid);

            string key = fm.gameObject.name;
            if (!maxGapByFlock.TryGetValue(key, out float prevMax) || gap > prevMax)
                maxGapByFlock[key] = gap;
            sampleCountByFlock[key] = sampleCountByFlock.TryGetValue(key, out int n) ? n + 1 : 1;
            if (gap > MaxAllowedSeparation)
                lastBreachByFlock[key] = Time.time;
        }
    }

    private static Vector3 ComputeFollowerCentroid(FlockManager fm, BoidAgent leader)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var b in fm.Boids)
        {
            if (b == null || b == leader) continue;
            sum += b.Position;
            count++;
        }
        return count > 0 ? sum / count : leader.Position;
    }

    // ── Reflection helpers (mirror BatchRunnerSmokeTest) ──

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
