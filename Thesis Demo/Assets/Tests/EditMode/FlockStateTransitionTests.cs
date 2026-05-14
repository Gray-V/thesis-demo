using NUnit.Framework;
using static FlockStateResolver;

/// <summary>
/// Pure-function tests for FlockStateResolver — the priority tree that decides
/// PureBOIDS flock state transitions. Mirrors GoalPriorityLogicTests so conditions
/// 2 and 4 fire analogous behaviors from the same stimulus.
/// </summary>
[TestFixture]
public class FlockStateTransitionTests
{
    // Shared default inputs — override per-test via named args.
    private static FlockBehavior Resolve(
        float hp = 0.8f,
        bool hasTarget = true,
        bool playerNearby = true,
        float playerDist = 6f,
        bool isRanged = false,
        bool attackSlotsSaturated = false,
        bool meleeAttackActive = false,
        bool rangedAttackActive = false,
        float maxDistanceFromCentroid = 3f,
        int boidCount = 5,
        bool clusteredEnoughToEngage = true,
        bool flockCooldownActive = false)
    {
        return FlockStateResolver.Resolve(
            healthPercent: hp,
            hasTarget: hasTarget,
            playerNearby: playerNearby,
            playerDist: playerDist,
            isRanged: isRanged,
            attackSlotsSaturated: attackSlotsSaturated,
            meleeAttackActive: meleeAttackActive,
            rangedAttackActive: rangedAttackActive,
            maxDistanceFromCentroid: maxDistanceFromCentroid,
            boidCount: boidCount,
            clusteredEnoughToEngage: clusteredEnoughToEngage,
            flockCooldownActive: flockCooldownActive);
    }

    [Test]
    public void CriticalHealth_PlayerNearby_Scatters()
    {
        Assert.AreEqual(FlockBehavior.Scattering, Resolve(hp: 0.10f));
    }

    [Test]
    public void LowHealth_PlayerNearby_Flees()
    {
        Assert.AreEqual(FlockBehavior.Fleeing, Resolve(hp: 0.25f));
    }

    [Test]
    public void Ranged_PlayerInsideKiteDistance_Kites()
    {
        Assert.AreEqual(FlockBehavior.Kiting, Resolve(isRanged: true, playerDist: 5f));
    }

    [Test]
    public void Ranged_PlayerBeyondKiteDistance_Engages()
    {
        Assert.AreEqual(FlockBehavior.Engaging, Resolve(isRanged: true, playerDist: 12f));
    }

    [Test]
    public void Melee_PlayerClose_Engages()
    {
        Assert.AreEqual(FlockBehavior.Engaging, Resolve(isRanged: false, playerDist: 5f));
    }

    [Test]
    public void NoPlayerNearby_SpreadFlock_Regroups()
    {
        Assert.AreEqual(FlockBehavior.Regrouping,
            Resolve(playerNearby: false, maxDistanceFromCentroid: 25f, boidCount: 4));
    }

    [Test]
    public void SlotsSaturated_DuringActiveAttack_Flanks()
    {
        Assert.AreEqual(FlockBehavior.Flanking,
            Resolve(attackSlotsSaturated: true, meleeAttackActive: true));
    }

    [Test]
    public void PlayerAtMidRange_NoActiveAttack_Guards()
    {
        // playerDist in [15, 30], playerNearby=false (beyond aggro, but guard
        // band captures the mid-range) → Guarding.
        Assert.AreEqual(FlockBehavior.Guarding,
            Resolve(playerNearby: false, playerDist: 22f, clusteredEnoughToEngage: false));
    }

    [Test]
    public void NoTarget_Idles()
    {
        Assert.AreEqual(FlockBehavior.Idle,
            Resolve(hasTarget: false, playerNearby: false, clusteredEnoughToEngage: false));
    }

    [Test]
    public void HasTarget_NotClusteredYet_Groups()
    {
        Assert.AreEqual(FlockBehavior.Grouping,
            Resolve(playerNearby: false, playerDist: 50f, clusteredEnoughToEngage: false));
    }

    // ── Priority-ordering invariants ──

    [Test]
    public void ScatterBeatsFleeWhenBothWouldFire()
    {
        // HP < critical AND < flee threshold — critical should win.
        Assert.AreEqual(FlockBehavior.Scattering, Resolve(hp: 0.10f));
    }

    [Test]
    public void FleeBeatsKiteWhenBothWouldFire()
    {
        // Ranged + close + low HP — Flee priority over Kite.
        Assert.AreEqual(FlockBehavior.Fleeing,
            Resolve(hp: 0.25f, isRanged: true, playerDist: 5f));
    }

    [Test]
    public void KiteBeatsEngageForRangedTooClose()
    {
        Assert.AreEqual(FlockBehavior.Kiting, Resolve(isRanged: true, playerDist: 5f));
    }

    [Test]
    public void EngageBeatsGuardWhenPlayerInBoth()
    {
        // playerDist in [15,30] and playerNearby=true and clustered → Engage wins.
        Assert.AreEqual(FlockBehavior.Engaging,
            Resolve(playerDist: 18f, clusteredEnoughToEngage: true));
    }

    // ── Flanking parity (Condition 2 ↔ Conditions 3/4) ──
    //
    // GOAP flanks proactively when `playerNearby && !cooldownReady && flockCount>=3`.
    // Before the fix, FlockStateResolver only flanked when a wave was ALREADY active.
    // These tests exercise the three proactive-flank triggers added in the fix.

    [Test]
    public void Flanks_Proactively_OnFlockCooldown()
    {
        // Flock on post-attack cooldown but otherwise healthy and in range → should
        // flank, mirroring GOAP's !cooldownReady flank trigger.
        Assert.AreEqual(FlockBehavior.Flanking,
            Resolve(flockCooldownActive: true, boidCount: 5));
    }

    [Test]
    public void Flanks_Proactively_WhenSlotsSaturated_NoActiveWave()
    {
        // Individual attack slots all in WindUp — a wave hasn't started but the
        // flock can't add more attackers. Mirrors GOAP's !attackSlotAvailable flank.
        Assert.AreEqual(FlockBehavior.Flanking,
            Resolve(attackSlotsSaturated: true, meleeAttackActive: false, rangedAttackActive: false, boidCount: 5));
    }

    [Test]
    public void DoesNotFlank_WhenFlockTooSmall()
    {
        // GOAP's flockCount>=3 gate applies here too — 2 boids on cooldown should
        // Engage (close) rather than flank.
        Assert.AreEqual(FlockBehavior.Engaging,
            Resolve(flockCooldownActive: true, boidCount: 2));
    }

    [Test]
    public void Flanks_OverEngage_WhenCooldownActive_AndClustered()
    {
        // Priority: Flank (can't engage) should beat Engage when the blocker is active.
        Assert.AreEqual(FlockBehavior.Flanking,
            Resolve(flockCooldownActive: true, boidCount: 4, clusteredEnoughToEngage: true));
    }

    [Test]
    public void ScatterBeatsFlank_AtCriticalHealth()
    {
        // Even with flock on cooldown, critical-HP scatter outranks flank.
        Assert.AreEqual(FlockBehavior.Scattering,
            Resolve(hp: 0.10f, flockCooldownActive: true, boidCount: 5));
    }

    [Test]
    public void FleeBeatsFlank_AtLowHealth()
    {
        Assert.AreEqual(FlockBehavior.Fleeing,
            Resolve(hp: 0.25f, flockCooldownActive: true, boidCount: 5));
    }

    [Test]
    public void KiteBeatsFlank_ForRangedAtClose()
    {
        Assert.AreEqual(FlockBehavior.Kiting,
            Resolve(isRanged: true, playerDist: 5f, flockCooldownActive: true, boidCount: 5));
    }
}
