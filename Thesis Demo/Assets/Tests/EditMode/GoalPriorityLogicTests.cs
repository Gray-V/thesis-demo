using NUnit.Framework;
using static GoalPriorityResolver;

[TestFixture]
public class GoalPriorityLogicTests
{
    // ── Scatter (critical health + player nearby) ──

    [Test]
    public void CriticalHealth_ReturnsScatter()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.10f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Scatter, goal);
    }

    // ── Flee (low health + player nearby) ──

    [Test]
    public void LowHealth_ReturnsFlee()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.25f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Flee, goal);
    }

    // ── Attack (melee, cooldown ready) ──

    [Test]
    public void CooldownReady_Melee_ReturnsAttack()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Attack, goal);
    }

    // ── Kite (ranged, close to player) ──

    [Test]
    public void CooldownReady_Ranged_Close_ReturnsKite()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: true,
            isRanged: true, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Kite, goal);
    }

    // ── RangedAttack (ranged, far enough from player) ──

    [Test]
    public void CooldownReady_Ranged_Far_ReturnsRangedAttack()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: true,
            isRanged: true, playerDist: 12f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.RangedAttack, goal);
    }

    // ── Regroup (isolated from flock) ──

    [Test]
    public void Isolated_ReturnsRegroup()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 50f, isIsolated: true, flockCount: 5);

        Assert.AreEqual(GoalType.Regroup, goal);
    }

    // ── Flank (cooldown not ready, player nearby, enough agents) ──

    [Test]
    public void MultipleAgents_CooldownActive_ReturnsFlank()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 10f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Flank, goal);
    }

    // ── Guard (player at mid range) ──

    [Test]
    public void MidRange_ReturnsGuard()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 20f, isIsolated: false, flockCount: 1);

        Assert.AreEqual(GoalType.Guard, goal);
    }

    // ── Wander (nothing else triggers) ──

    [Test]
    public void Nothing_ReturnsWander()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 50f, isIsolated: false, flockCount: 1);

        Assert.AreEqual(GoalType.Wander, goal);
    }

    // ── Priority ordering ──

    [Test]
    public void ScatterBeatsFleeAtCriticalHealth()
    {
        // health=10% is below both scatter (15%) and flee (30%) thresholds
        // Scatter should win because it has higher priority
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.10f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Scatter, goal, "Scatter should beat Flee at critical health");
    }

    [Test]
    public void FleeBeatsAttackAtLowHealth()
    {
        // health=25%, cooldown ready — Flee should beat Attack
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.25f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Flee, goal, "Flee should beat Attack at low health");
    }

    // ── Leader goal delegates to same logic ──

    [Test]
    public void LeaderGoal_DelegatesToSameLogic()
    {
        var goapGoal = ResolveGOAPBoidGoal(
            0.10f, true, true, false, 5f, false, 5);
        var leaderGoal = ResolveLeaderGoal(
            0.10f, true, true, false, 5f, false, 5);

        Assert.AreEqual(goapGoal, leaderGoal, "Leader should resolve same goal as GOAP boid");
    }

    // ── Boundary thresholds ──

    [Test]
    public void HealthExactlyAtCriticalThreshold_DoesNotScatter()
    {
        // At exactly 0.15 (not below), scatter should NOT trigger
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.15f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreNotEqual(GoalType.Scatter, goal, "Exactly at threshold should NOT scatter");
    }

    [Test]
    public void HealthJustBelowFleeThreshold_ReturnsFlee()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.29f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Flee, goal);
    }

    [Test]
    public void FlankRequiresAtLeast3Agents()
    {
        // Only 2 agents — should NOT flank
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 10f, isIsolated: false, flockCount: 2);

        Assert.AreNotEqual(GoalType.Flank, goal);
    }

    [Test]
    public void GuardRange_OutsideOuter_ReturnsWander()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 35f, isIsolated: false, flockCount: 1);

        Assert.AreEqual(GoalType.Wander, goal, "Outside guard outer range should wander");
    }

    [Test]
    public void CustomThresholds_Respected()
    {
        // Custom critical health = 0.5, so health=0.4 triggers scatter
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.4f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 1,
            criticalHealthThreshold: 0.5f);

        Assert.AreEqual(GoalType.Scatter, goal, "Custom critical threshold should be respected");
    }

    // ── Hysteresis tests (added 2026-05-06) ─────────────────────────────────
    //
    // Reproduces the Flee↔Regroup oscillation observed in the 2026-05-06 N=200
    // smoke batch. With the resolver's default hysteresis (zero buffer), the
    // resolver flips Flee↔Regroup at the threshold boundary frame to frame.
    // With a positive hysteresis buffer it stays in Flee until health recovers
    // past threshold + buffer.

    [Test]
    public void FleeHysteresis_HealthAtThreshold_StaysFlee_WhenPreviouslyFleeing()
    {
        // healthPercent at exact fleeThreshold (0.3) — the boundary that produced
        // the smoke-batch oscillation. Without hysteresis the resolver would not
        // re-pick Flee here (the < check fails). With hysteresis + previousGoal=Flee
        // the resolver should keep us in Flee.
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.30f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 25f, isIsolated: true, flockCount: 5,
            previousGoal: GoalType.Flee, hysteresisBuffer: 0.05f);

        Assert.AreEqual(GoalType.Flee, goal,
            "At the fleeThreshold boundary with previousGoal=Flee, hysteresis should keep us in Flee instead of falling through to Regroup");
    }

    [Test]
    public void FleeHysteresis_HealthBelowExitThreshold_StaysFlee()
    {
        // health = 0.34 (below 0.30 + 0.05 = 0.35 exit threshold) → still Flee
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.34f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 25f, isIsolated: true, flockCount: 5,
            previousGoal: GoalType.Flee, hysteresisBuffer: 0.05f);

        Assert.AreEqual(GoalType.Flee, goal,
            "Health below exit threshold (0.30 + 0.05) with previousGoal=Flee should stay Flee");
    }

    [Test]
    public void FleeHysteresis_HealthAboveExitThreshold_ReleasesFlee()
    {
        // health = 0.36 (above 0.30 + 0.05 = 0.35 exit threshold) → release Flee.
        // With isIsolated=true and player not nearby, falls through to Regroup.
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.36f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 35f, isIsolated: true, flockCount: 5,
            previousGoal: GoalType.Flee, hysteresisBuffer: 0.05f);

        Assert.AreEqual(GoalType.Regroup, goal,
            "Health above exit threshold should release the Flee hysteresis and fall through to Regroup");
    }

    [Test]
    public void FleeHysteresis_PlayerLeaves_StaysFlee()
    {
        // The other half of the oscillation cause: previously the leader's Flee path
        // was gated on playerNearby. When the leader fled out of the player's 30 m
        // perception sphere, playerNearby flipped false → Flee de-selected → Regroup.
        // Hysteresis should keep Flee active even when player perception is lost,
        // since the flock is still at low health and shouldn't drop the defensive posture.
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.25f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 50f, isIsolated: true, flockCount: 5,
            previousGoal: GoalType.Flee, hysteresisBuffer: 0.05f);

        Assert.AreEqual(GoalType.Flee, goal,
            "Sticky Flee should hold even when the player leaves perception range, as long as health is still below exit threshold");
    }

    [Test]
    public void FleeHysteresis_Disabled_PreservesPreFixBehavior()
    {
        // With hysteresisBuffer=0 (default), the resolver behaves as it did before
        // the 2026-05-06 fix. Health at 0.30 is NOT < 0.30, so Flee doesn't fire,
        // and isIsolated falls through to Regroup. Locks the back-compat default.
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.30f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 25f, isIsolated: true, flockCount: 5,
            previousGoal: GoalType.Flee, hysteresisBuffer: 0f);

        Assert.AreEqual(GoalType.Regroup, goal,
            "With hysteresis disabled (buffer=0) the resolver preserves pre-fix behavior — Flee is not sticky");
    }

    [Test]
    public void ScatterHysteresis_HealthAtCritical_StaysScatter()
    {
        // Same hysteresis applied to the criticalHealthThreshold boundary so
        // Scatter doesn't oscillate the same way Flee did.
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.15f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5,
            previousGoal: GoalType.Scatter, hysteresisBuffer: 0.05f);

        Assert.AreEqual(GoalType.Scatter, goal,
            "At criticalThreshold with previousGoal=Scatter, hysteresis should keep us in Scatter");
    }

    [Test]
    public void Hysteresis_NotSticky_WhenPreviousGoalDifferent()
    {
        // previousGoal=Wander; health=0.30; should NOT be sticky-Flee. Resolver runs
        // the standard chain — at exact threshold, Flee doesn't fire (< check), falls
        // through to Regroup.
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.30f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 25f, isIsolated: true, flockCount: 5,
            previousGoal: GoalType.Wander, hysteresisBuffer: 0.05f);

        Assert.AreEqual(GoalType.Regroup, goal,
            "Hysteresis should only apply when previousGoal matches the sticky state");
    }
}
