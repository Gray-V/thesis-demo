using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for the reaction-time semantics of <see cref="BehavioralMetricsCollector"/>.
/// These are *not* tests for the new diagnostic instrumentation (those live in
/// <c>LeaderGoapBrainDiagnosticTests</c> and <c>LeaderReactionPathTests</c>) — they
/// pin down the existing metric definition that v1 was scored against, so any
/// future refactor of the gate logic at lines 161-179 of BehavioralMetricsCollector
/// fails loudly rather than silently shifting v2 numbers vs v1.
///
/// Why this matters for the Leader investigation: the metric measures
/// "first StimulusAcquired → first non-passive GoalChange per flockId". The
/// v1 Leader median of 857 ms is the time the *leader's* goal-resolver took
/// to flip away from Wander/Idle/Grouping after the flock-level stimulus.
/// These tests confirm that semantics still holds, so the four-bucket
/// diagnostic budget (StimulusAcquired → LeaderPerceives → GoalChange →
/// LeaderActionStart) is sub-decomposing the same 857 ms.
/// </summary>
[TestFixture]
public class BehavioralMetricsReactionTimeTests
{
    private GameObject collectorGO;
    private BehavioralMetricsCollector collector;

    [SetUp]
    public void SetUp()
    {
        collectorGO = new GameObject("TestCollector");
        collector = collectorGO.AddComponent<BehavioralMetricsCollector>();
        // EditMode does not auto-fire MonoBehaviour Awake; StartRecording is
        // public and idempotent, and is the entry point all the production
        // call sites use, so just calling it directly is the cleanest setup.
        collector.StartRecording(runId: 0, seed: 0, mode: BenchmarkMode.Combat);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(collectorGO);
    }

    // ── Happy path: stimulus then active-goal change → reaction time recorded ──

    [Test]
    public void StimulusThenActiveGoalChange_RecordsReactionTime()
    {
        collector.LogEvent("StimulusAcquired", "TestFlock", 0, "Target=Player");
        collector.LogEvent("GoalChange", "TestLeader", 0, "Wander→Attack");

        float ms = collector.GetReactionTimeMs(0);
        Assert.GreaterOrEqual(ms, 0f,
            "After a stimulus and a non-passive goal change, reaction time "
            + "must be a non-negative number (clamped at 0 if same-frame).");
    }

    // ── Gate: goal change without stimulus → no reaction time ──
    //
    // This is the BehavioralMetricsCollector.cs:174 ordering guard. Without
    // it, a leader that wakes up mid-trial with a goal already set (e.g.
    // re-spawned after death) would log a "response" with no preceding
    // stimulus, producing a negative or zero delta clamped to 0.

    [Test]
    public void GoalChangeBeforeStimulus_StillNoReactionTime()
    {
        collector.LogEvent("GoalChange", "TestLeader", 0, "Wander→Attack");
        // No StimulusAcquired ever fires for this flock.
        Assert.AreEqual(-1f, collector.GetReactionTimeMs(0),
            "Response without a preceding stimulus must report -1 (sentinel "
            + "for 'no reaction').");
    }

    [Test]
    public void StimulusAfterGoalChange_DoesNotBackfillResponse()
    {
        collector.LogEvent("GoalChange", "TestLeader", 0, "Wander→Attack");
        collector.LogEvent("StimulusAcquired", "TestFlock", 0, "Target=Player");
        Assert.AreEqual(-1f, collector.GetReactionTimeMs(0),
            "A stimulus that fires AFTER a goal change must not retroactively "
            + "make that goal change count as the response — the leader's "
            + "goal change was caused by something else.");
    }

    // ── Filter: passive-goal transitions are not responses ──
    //
    // Wander/Idle/Grouping are the *passive* states. Transitioning to one of
    // them after a stimulus is not a "reaction" — it usually means the leader
    // gave up on the player or the flock dispersed. The metric must skip
    // these transitions and wait for a real active-goal switch.

    [Test]
    public void PassiveGoalChange_AfterStimulus_NoReactionTime()
    {
        collector.LogEvent("StimulusAcquired", "TestFlock", 0, "Target=Player");
        collector.LogEvent("GoalChange", "TestLeader", 0, "Attack→Wander");
        collector.LogEvent("GoalChange", "TestLeader", 0, "Wander→Idle");
        collector.LogEvent("GoalChange", "TestLeader", 0, "Idle→Grouping");
        Assert.AreEqual(-1f, collector.GetReactionTimeMs(0),
            "Transitions into Wander/Idle/Grouping must not count as responses.");
    }

    [Test]
    public void ActiveGoalChange_AfterPassive_StillRecordsResponse()
    {
        collector.LogEvent("StimulusAcquired", "TestFlock", 0, "Target=Player");
        collector.LogEvent("GoalChange", "TestLeader", 0, "Wander→Idle");      // skipped
        collector.LogEvent("GoalChange", "TestLeader", 0, "Idle→Attack");      // first response
        Assert.GreaterOrEqual(collector.GetReactionTimeMs(0), 0f,
            "A passive-then-active sequence must latch the first active goal "
            + "as the response — passive transitions don't 'consume' the "
            + "response slot.");
    }

    // ── Per-flock isolation: melee and ranged reactions are tracked separately ──
    //
    // Pre-fix v1 had both leaders writing flockId=0, which caused the ranged
    // leader's response to "land" on the melee bucket and overwrite nothing.
    // Result: ReactionRangedMs = -1 across every trial. Test guards against
    // any regression that re-couples the buckets.

    [Test]
    public void RangedFlockResponse_DoesNotLeakIntoMeleeBucket()
    {
        collector.LogEvent("StimulusAcquired", "TestFlock", 1, "Target=Player");
        collector.LogEvent("GoalChange", "TestRangedLeader", 1, "Wander→Kite");
        // Melee flock (flockId=0) never saw a stimulus.
        Assert.AreEqual(-1f, collector.GetReactionTimeMs(0),
            "Melee bucket must remain at -1 when only the ranged flock had a stimulus.");
        Assert.GreaterOrEqual(collector.GetReactionTimeMs(1), 0f,
            "Ranged bucket must hold the ranged leader's reaction.");
    }

    // ── First-response-wins semantics ──

    [Test]
    public void SecondActiveGoalChange_DoesNotOverwriteFirstResponse()
    {
        collector.LogEvent("StimulusAcquired", "TestFlock", 0, "Target=Player");
        collector.LogEvent("GoalChange", "TestLeader", 0, "Wander→Attack");
        float first = collector.GetReactionTimeMs(0);
        // Wait one frame's worth of time, then fire another active-goal change.
        // Time.time advances even in EditMode, but slowly — we don't need a
        // wall-clock delta, just to check that the second event doesn't
        // replace the first.
        collector.LogEvent("GoalChange", "TestLeader", 0, "Attack→Kite");
        float second = collector.GetReactionTimeMs(0);
        Assert.AreEqual(first, second, 1e-4f,
            "Reaction time must reflect the FIRST active goal change post-"
            + "stimulus — subsequent transitions are noise for this metric.");
    }
}
