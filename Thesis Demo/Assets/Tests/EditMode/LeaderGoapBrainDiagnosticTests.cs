using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for the leader reaction-time diagnostic surface added on
/// 2026-05-05 to investigate the v1 batch finding that BOIDSWithGOAPLeader has
/// a median melee reaction of 857–2602 ms across N (vs 2–43 ms for PureBOIDS
/// and 5–59 ms for GOAPWithBOIDSMovement). See wiki/experiments/v1-batch-2026-05-05.md
/// surprises section and wiki/todos.md item #1.
///
/// These tests cover the *static / pure* parts of the instrumentation: the
/// flockId mapping helper, the default state of the toggle, and the FlockManager
/// hand-off field. End-to-end verification that the four diagnostic events
/// actually fire in order during a real Leader trial lives in PlayMode
/// (LeaderReactionPathTests).
/// </summary>
[TestFixture]
public class LeaderGoapBrainDiagnosticTests
{
    private GameObject boidGO;
    private BoidAgent boid;
    private BoidSettings settings;

    [SetUp]
    public void SetUp()
    {
        boidGO = new GameObject("TestLeader");
        boid = boidGO.AddComponent<BoidAgent>();
        settings = ScriptableObject.CreateInstance<BoidSettings>();
        boid.settings = settings;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(boidGO);
        Object.DestroyImmediate(settings);
        // Reset the static toggle so a test that flips it can't bleed into the
        // next test or into other test fixtures running in the same session.
        LeaderGoapBrain.DiagnosticLogging = false;
    }

    // ── ResolveFlockId — null-safety + enum mapping ──
    //
    // Locks down the regression that left ReactionRangedMs perpetually -1 in
    // pre-fix v1 trial summaries: a hardcoded flockId=0 collapsed both leaders'
    // events into one bucket, so the response handler's "first non-passive
    // GoalChange per flockId" gate fired only for melee. Any future refactor
    // that breaks the enum mapping will fail these tests before it eats CPU
    // on a batch run.

    [Test]
    public void ResolveFlockId_NullBoid_ReturnsZero()
    {
        Assert.AreEqual(0, LeaderGoapBrain.ResolveFlockId(null),
            "Null boid must fall back to flockId=0 rather than throwing.");
    }

    [Test]
    public void ResolveFlockId_NullSettings_ReturnsZero()
    {
        boid.settings = null;
        Assert.AreEqual(0, LeaderGoapBrain.ResolveFlockId(boid),
            "Boid with null settings must fall back to flockId=0.");
    }

    [Test]
    public void ResolveFlockId_MeleeFlock_ReturnsZero()
    {
        settings.flockType = FlockType.Melee;
        Assert.AreEqual(0, LeaderGoapBrain.ResolveFlockId(boid),
            "FlockType.Melee → flockId 0 (matches BehavioralMetricsCollector convention).");
    }

    [Test]
    public void ResolveFlockId_RangedFlock_ReturnsOne()
    {
        settings.flockType = FlockType.Ranged;
        Assert.AreEqual(1, LeaderGoapBrain.ResolveFlockId(boid),
            "FlockType.Ranged → flockId 1. Regression net for the pre-fix bug "
            + "that left ReactionRangedMs = -1 in v1 trial summaries.");
    }

    // ── Toggle default-off contract ──
    //
    // The diagnostic adds 4–5 events per stimulus per flock to the EventLog
    // CSV when on. Default-off is what guarantees v2 batch CSVs stay byte-for-
    // byte comparable to v1 unless the operator explicitly opts in via the
    // Thesis → Toggle Leader Diagnostic editor menu.

    [Test]
    public void DiagnosticLogging_StaticDefault_IsFalse()
    {
        // TearDown resets it explicitly between tests; this test asserts the
        // *initial* value before any test or scene flips it. Effectively a
        // belt-and-suspenders check: if a future commit changes the default,
        // every existing batch's reproducibility claim moves quietly.
        // We can't observe "the value at static-init time" after TearDown has
        // run, so this test only meaningfully runs first within the fixture
        // (NUnit alphabetises by default → 'D' < 'F' < 'L' < 'R' so this runs
        // before any Resolve* test). The TearDown reset keeps it honest.
        Assert.IsFalse(LeaderGoapBrain.DiagnosticLogging,
            "LeaderGoapBrain.DiagnosticLogging must default to false. "
            + "If you flipped it on for a one-off investigation, flip it back "
            + "before committing.");
    }

    // ── FlockManager.pendingFollowerReactTime sentinel ──
    //
    // The leader-goal-change → next-LateUpdate hand-off uses -1f as "no event
    // pending". This test guards against the field being initialised to 0f or
    // any other value that the LateUpdate gate (>= 0f) would treat as pending.

    [Test]
    public void FlockManager_pendingFollowerReactTime_DefaultIsNegativeOne()
    {
        var fmGO = new GameObject("TestFlockManager");
        var fm = fmGO.AddComponent<FlockManager>();
        Assert.AreEqual(-1f, fm.pendingFollowerReactTime, 1e-6f,
            "pendingFollowerReactTime must default to -1f sentinel so the "
            + "LateUpdate gate (>= 0f) doesn't fire FollowerReact spuriously "
            + "before the leader has ever signalled a goal change.");
        Object.DestroyImmediate(fmGO);
    }
}
