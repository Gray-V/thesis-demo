using System;
using System.Reflection;
using NUnit.Framework;

/// <summary>
/// Constants regression net for all nine Leader GOAP actions. These tests use
/// reflection to read each action's <c>private const</c> tuning numbers and
/// assert the value, so a future commit that silently retunes (e.g. drops
/// LeaderAttackAction.TriggerDistance from 6 to 4) fails CI before it lands
/// in a batch run.
///
/// Why a regression net rather than a behavioural test: the constants are
/// load-bearing for the §3.4 System Design tables and the §5 Results combat
/// numbers, but they're <c>private</c>, scattered across nine files, and
/// changeable in seconds. The four-bucket diagnostic batch on 2026-05-05
/// surfaced exactly this class of silent-drift bug (LeaderGoapBrain's
/// goal-request switch missing the RangedAttack case) — locking down the
/// numeric surface area is the cheapest way to keep that class of bug from
/// recurring elsewhere.
///
/// Behavioural lifecycle coverage (Start → Perform → Complete/Stop → End)
/// for the two highest-stakes actions (LeaderAttackAction and
/// LeaderRangedAttackAction) lives in PlayMode (LeaderActionLifecycleTests).
/// The other seven actions are covered here at constants depth only — they
/// don't directly produce thesis headline numbers, so the cost/benefit of
/// PlayMode lifecycle coverage doesn't pencil out before defense.
///
/// Created in response to the 2026-05-06 audit finding that no existing
/// fixture covered the Leader actions' tuning constants. See
/// wiki/todos.md "v1 batch follow-ups" → instrumentation hygiene.
/// </summary>
[TestFixture]
public class LeaderActionConstantsTests
{
    // Reflection helper — the constants are private, but the field metadata
    // is still emitted to the type, so BindingFlags.NonPublic | Static reads
    // them without any source-side changes (no [InternalsVisibleTo], no
    // visibility downgrade, no test-only #if).
    private static T ReadConst<T>(Type t, string name)
    {
        FieldInfo f = t.GetField(name,
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.GetField);
        Assert.IsNotNull(f,
            $"{t.Name}.{name} not found via reflection — was it renamed or "
            + "removed? Update this test if the rename is intentional.");
        Assert.IsTrue(f.IsLiteral,
            $"{t.Name}.{name} is no longer a const. If it was promoted to a "
            + "tunable field, move the assertion to a behavioural test.");
        return (T)f.GetRawConstantValue();
    }

    // ── LeaderAttackAction ──
    // Three-phase melee: Approach → WindUp → Charge → damage on contact.
    // Trigger distance and contact distance bound the §5 melee-hit windows;
    // wind-up + charge durations bound the per-attack timing in EventLog.

    [Test]
    public void LeaderAttackAction_TriggerDistance_Is6()
    {
        Assert.AreEqual(6f, ReadConst<float>(typeof(LeaderAttackAction), "TriggerDistance"),
            "Approach→WindUp transition distance. Tweaking this changes the "
            + "median time-to-first-hit reported in §5 Combat results.");
    }

    [Test]
    public void LeaderAttackAction_WindUpDuration_Is04()
    {
        Assert.AreEqual(0.4f, ReadConst<float>(typeof(LeaderAttackAction), "WindUpDuration"));
    }

    [Test]
    public void LeaderAttackAction_ChargeDuration_Is06()
    {
        Assert.AreEqual(0.6f, ReadConst<float>(typeof(LeaderAttackAction), "ChargeDuration"));
    }

    [Test]
    public void LeaderAttackAction_ChargeSpeedMultiplier_Is3()
    {
        Assert.AreEqual(3f, ReadConst<float>(typeof(LeaderAttackAction), "ChargeSpeedMultiplier"),
            "Charge phase moves at 3× maxSpeed. Load-bearing for whether the "
            + "leader can actually close on a kiting player at the trial-end "
            + "FPS budget.");
    }

    [Test]
    public void LeaderAttackAction_DamageContactDistance_Is2()
    {
        Assert.AreEqual(2f, ReadConst<float>(typeof(LeaderAttackAction), "DamageContactDistance"));
    }

    [Test]
    public void LeaderAttackAction_PhaseEnum_HasThreeMembers()
    {
        var members = Enum.GetNames(typeof(LeaderAttackAction.Phase));
        Assert.AreEqual(3, members.Length,
            "Adding or removing a Phase reshapes the action's state machine "
            + "and almost certainly invalidates EventLog post-processing.");
        CollectionAssert.AreEquivalent(
            new[] { "Approach", "WindUp", "Charge" }, members);
    }

    // ── LeaderRangedAttackAction ──
    // Two-phase ranged: Approach → Fire. Created 2026-05-05 to fix the v1
    // ranged-leader Wandering bug. Projectile constants must match the
    // BoidSettings.flockProjectileDamage = 30 invariant the §5 numbers cite.

    [Test]
    public void LeaderRangedAttackAction_FiringRange_Is12()
    {
        Assert.AreEqual(12f, ReadConst<float>(typeof(LeaderRangedAttackAction), "FiringRange"),
            "Matches BoidSettings.flockAttackTriggerDistance = 12 for ranged "
            + "flocks. If they drift apart, the leader will fire at a different "
            + "range than the followers, breaking the per-flock attack symmetry "
            + "the §3 system-design section claims.");
    }

    [Test]
    public void LeaderRangedAttackAction_ApproachTimeout_Is8()
    {
        Assert.AreEqual(8f, ReadConst<float>(typeof(LeaderRangedAttackAction), "ApproachTimeout"));
    }

    [Test]
    public void LeaderRangedAttackAction_ProjectileDamage_Is30()
    {
        Assert.AreEqual(30f, ReadConst<float>(typeof(LeaderRangedAttackAction), "ProjectileDamage"),
            "Must match BoidSettings.flockProjectileDamage = 30 — the ranged "
            + "leader fires using the same damage value as a follower volley. "
            + "Drift here would silently inflate or deflate TotalDamageToPlayer "
            + "in v2 trials.");
    }

    [Test]
    public void LeaderRangedAttackAction_ProjectileSpeed_Is15()
    {
        Assert.AreEqual(15f, ReadConst<float>(typeof(LeaderRangedAttackAction), "ProjectileSpeed"),
            "Matches BoidSettings.projectileSpeed = 15 for ranged followers.");
    }

    [Test]
    public void LeaderRangedAttackAction_ProjectileTurnSpeed_Is5()
    {
        Assert.AreEqual(5f, ReadConst<float>(typeof(LeaderRangedAttackAction), "ProjectileTurnSpeed"));
    }

    [Test]
    public void LeaderRangedAttackAction_PhaseEnum_HasApproachAndFire()
    {
        var members = Enum.GetNames(typeof(LeaderRangedAttackAction.Phase));
        Assert.AreEqual(2, members.Length);
        CollectionAssert.AreEquivalent(new[] { "Approach", "Fire" }, members);
    }

    // ── LeaderKiteAction ──
    // Mirrors RangedAttack's projectile constants. The min/max distances are
    // *fallbacks* — the action prefers BoidSettings.kiteMinDistance /
    // .kiteMaxDistance when settings is non-null, so these tests lock down
    // the no-settings safety-net values.

    [Test]
    public void LeaderKiteAction_KiteMinDistance_Fallback_Is8()
    {
        Assert.AreEqual(8f, ReadConst<float>(typeof(LeaderKiteAction), "KiteMinDistance"),
            "Fallback used only when BoidSettings is null. Drift in the "
            + "fallback wouldn't affect normal trials but would change "
            + "behaviour in any test or scene that omits settings.");
    }

    [Test]
    public void LeaderKiteAction_KiteMaxDistance_Fallback_Is15()
    {
        Assert.AreEqual(15f, ReadConst<float>(typeof(LeaderKiteAction), "KiteMaxDistance"));
    }

    [Test]
    public void LeaderKiteAction_ProjectileDamage_Is30()
    {
        Assert.AreEqual(30f, ReadConst<float>(typeof(LeaderKiteAction), "ProjectileDamage"),
            "Must match LeaderRangedAttackAction.ProjectileDamage so kite-fire "
            + "and approach-fire deal identical damage per shot.");
    }

    [Test]
    public void LeaderKiteAction_ProjectileSpeed_Is15()
    {
        Assert.AreEqual(15f, ReadConst<float>(typeof(LeaderKiteAction), "ProjectileSpeed"));
    }

    [Test]
    public void LeaderKiteAction_ProjectileTurnSpeed_Is5()
    {
        Assert.AreEqual(5f, ReadConst<float>(typeof(LeaderKiteAction), "ProjectileTurnSpeed"));
    }

    [Test]
    public void LeaderKiteAction_PhaseEnum_HasRetreatAndFire()
    {
        var members = Enum.GetNames(typeof(LeaderKiteAction.Phase));
        Assert.AreEqual(2, members.Length);
        CollectionAssert.AreEquivalent(new[] { "Retreat", "Fire" }, members);
    }

    // ── LeaderFleeAction ──
    // SafeDistance is the early-exit when the player is already far enough.
    // FleeTimer is randomised in Start() — covered in ReproducibilityTests.

    [Test]
    public void LeaderFleeAction_SafeDistance_Is40()
    {
        Assert.AreEqual(40f, ReadConst<float>(typeof(LeaderFleeAction), "SafeDistance"),
            "Player-distance threshold for early Completion. 40m matches the "
            + "leader's perception sphere (30m) + a buffer; tweaking it "
            + "changes how long Flee occupies the goal-occupancy histogram.");
    }

    // ── LeaderGuardAction ──

    [Test]
    public void LeaderGuardAction_HoldTolerance_Is2()
    {
        Assert.AreEqual(2f, ReadConst<float>(typeof(LeaderGuardAction), "HoldTolerance"));
    }

    [Test]
    public void LeaderGuardAction_SafetyTimeout_Is30()
    {
        Assert.AreEqual(30f, ReadConst<float>(typeof(LeaderGuardAction), "SafetyTimeout"),
            "30s is half a Combat-mode trial; if Guard ever holds for the "
            + "full timeout, half the trial sat idle. Worth keeping as the "
            + "documented worst case.");
    }

    // ── LeaderFlankAction ──
    // Restart-spam fix landed 2026-05-05 (Flank now holds with Continue when
    // at the slot rather than returning Completed). FlankTimer is the only
    // termination source once at-slot.

    [Test]
    public void LeaderFlankAction_SlotTolerance_Is3()
    {
        Assert.AreEqual(3f, ReadConst<float>(typeof(LeaderFlankAction), "SlotTolerance"));
    }

    [Test]
    public void LeaderFlankAction_FlankRadius_Is10()
    {
        Assert.AreEqual(10f, ReadConst<float>(typeof(LeaderFlankAction), "FlankRadius"),
            "Distance from player at which the leader holds the flank slot. "
            + "Smaller values pull followers into melee range automatically; "
            + "larger values keep the flank purely positional.");
    }

    // ── LeaderRegroupAction ──
    // Inline timer literal in Start() (8f) rather than a const — covered in
    // ReproducibilityTests via determinism check. Nothing to lock down here
    // beyond the file's existence, which the goal-mapping tests already cover.

    // ── LeaderScatterAction ──
    // Triggered only when health is critically low. ScatterTimer is randomised
    // (covered in ReproducibilityTests). Direction picker is in
    // ScatterDirectionPicker — its own test surface.

    // ── LeaderWanderAction ──

    [Test]
    public void LeaderWanderAction_ArrivalDistance_Is5()
    {
        Assert.AreEqual(5f, ReadConst<float>(typeof(LeaderWanderAction), "ArrivalDistance"));
    }

    [Test]
    public void LeaderWanderAction_MaxTime_Is12()
    {
        Assert.AreEqual(12f, ReadConst<float>(typeof(LeaderWanderAction), "MaxTime"),
            "Upper bound on Wander occupancy per dispatch. With the v1 ranged-"
            + "leader Wandering bug fixed, this should rarely run to timeout — "
            + "if v2 batches show Wander occupancy approaching 12s frequently, "
            + "investigate.");
    }

    // ── Cross-action invariants ──
    // These three values are repeated across LeaderRangedAttackAction and
    // LeaderKiteAction. Locking the equality avoids the silent-drift bug
    // where one action gets retuned and the other doesn't.

    [Test]
    public void RangedAndKite_ProjectileDamage_Match()
    {
        Assert.AreEqual(
            ReadConst<float>(typeof(LeaderRangedAttackAction), "ProjectileDamage"),
            ReadConst<float>(typeof(LeaderKiteAction), "ProjectileDamage"),
            "Both leader projectile actions must deal identical damage per "
            + "shot, otherwise the §5 narrative 'ranged leader = ranged "
            + "follower per-shot output' breaks asymmetrically depending on "
            + "which Goal happened to fire.");
    }

    [Test]
    public void RangedAndKite_ProjectileSpeed_Match()
    {
        Assert.AreEqual(
            ReadConst<float>(typeof(LeaderRangedAttackAction), "ProjectileSpeed"),
            ReadConst<float>(typeof(LeaderKiteAction), "ProjectileSpeed"));
    }

    [Test]
    public void RangedAndKite_ProjectileTurnSpeed_Match()
    {
        Assert.AreEqual(
            ReadConst<float>(typeof(LeaderRangedAttackAction), "ProjectileTurnSpeed"),
            ReadConst<float>(typeof(LeaderKiteAction), "ProjectileTurnSpeed"));
    }
}
