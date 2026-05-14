using NUnit.Framework;
using static FlockStateResolver;
using static GoalPriorityResolver;

/// <summary>
/// Thesis parity test: for any given world state, Condition 2's FlockStateResolver
/// and Conditions 3/4's GoalPriorityResolver must select analogous behaviors. A
/// mismatch here means the thesis's "same 9 behaviors, different decision
/// architectures" parity claim is false for that stimulus — which would
/// invalidate the cross-condition comparison.
///
/// Mapping (FlockBehavior → GoalType):
///   Scattering  → Scatter
///   Fleeing     → Flee
///   Kiting      → Kite
///   Engaging    → Attack / RangedAttack (melee vs ranged)
///   Flanking    → Flank
///   Guarding    → Guard
///   Regrouping  → Regroup
///   Grouping    → (no GOAP analogue — BOIDS intermediate; handled explicitly)
///   Idle        → Wander
///
/// The resolvers diverge by design on two points, both documented here:
///  (a) Grouping: PureBOIDS has an intermediate "move into engagement range" state
///      that GOAP doesn't need — GOAP Wanders/approaches via the action layer.
///  (b) Regrouping: PureBOIDS uses max-distance-from-centroid; GOAP uses an
///      isIsolated sensor per-agent. Driven by the same isolationThreshold.
/// </summary>
[TestFixture]
public class CrossConditionParityTests
{
    private static bool IsEquivalent(FlockBehavior fb, GoalType gt, bool isRanged)
    {
        switch (fb)
        {
            case FlockBehavior.Scattering:  return gt == GoalType.Scatter;
            case FlockBehavior.Fleeing:     return gt == GoalType.Flee;
            case FlockBehavior.Kiting:      return gt == GoalType.Kite;
            case FlockBehavior.Engaging:    return isRanged ? gt == GoalType.RangedAttack : gt == GoalType.Attack;
            case FlockBehavior.Flanking:    return gt == GoalType.Flank;
            case FlockBehavior.Guarding:    return gt == GoalType.Guard;
            case FlockBehavior.Regrouping:  return gt == GoalType.Regroup;
            case FlockBehavior.Idle:        return gt == GoalType.Wander;
            case FlockBehavior.Grouping:    return gt == GoalType.Wander || gt == GoalType.Attack || gt == GoalType.RangedAttack;
            default: return false;
        }
    }

    // ── Critical-health stimulus: both fire Scatter ──
    [Test]
    public void CriticalHealth_BothResolveScatter()
    {
        var fb = FlockStateResolver.Resolve(
            healthPercent: 0.10f, hasTarget: true, playerNearby: true, playerDist: 6f,
            isRanged: false, attackSlotsSaturated: false, meleeAttackActive: false,
            rangedAttackActive: false, maxDistanceFromCentroid: 3f, boidCount: 5,
            clusteredEnoughToEngage: true);
        var gt = ResolveGOAPBoidGoal(
            healthPercent: 0.10f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 6f, isIsolated: false, flockCount: 5);

        Assert.IsTrue(IsEquivalent(fb, gt, isRanged: false),
            $"Critical-health stimulus should resolve analogously. FlockBehavior={fb}, GoalType={gt}");
    }

    [Test]
    public void LowHealth_BothResolveFlee()
    {
        var fb = FlockStateResolver.Resolve(
            healthPercent: 0.25f, hasTarget: true, playerNearby: true, playerDist: 6f,
            isRanged: false, attackSlotsSaturated: false, meleeAttackActive: false,
            rangedAttackActive: false, maxDistanceFromCentroid: 3f, boidCount: 5,
            clusteredEnoughToEngage: true);
        var gt = ResolveGOAPBoidGoal(
            healthPercent: 0.25f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 6f, isIsolated: false, flockCount: 5);

        Assert.IsTrue(IsEquivalent(fb, gt, isRanged: false),
            $"Low-health stimulus should resolve analogously. FlockBehavior={fb}, GoalType={gt}");
    }

    [Test]
    public void RangedAndClose_BothResolveKite()
    {
        var fb = FlockStateResolver.Resolve(
            healthPercent: 0.8f, hasTarget: true, playerNearby: true, playerDist: 5f,
            isRanged: true, attackSlotsSaturated: false, meleeAttackActive: false,
            rangedAttackActive: false, maxDistanceFromCentroid: 3f, boidCount: 5,
            clusteredEnoughToEngage: true);
        var gt = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: true,
            isRanged: true, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.IsTrue(IsEquivalent(fb, gt, isRanged: true),
            $"Ranged-close stimulus should both resolve to Kite. FlockBehavior={fb}, GoalType={gt}");
    }

    [Test]
    public void MeleeInRange_BothResolveEngageOrAttack()
    {
        var fb = FlockStateResolver.Resolve(
            healthPercent: 0.8f, hasTarget: true, playerNearby: true, playerDist: 5f,
            isRanged: false, attackSlotsSaturated: false, meleeAttackActive: false,
            rangedAttackActive: false, maxDistanceFromCentroid: 3f, boidCount: 5,
            clusteredEnoughToEngage: true);
        var gt = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.IsTrue(IsEquivalent(fb, gt, isRanged: false),
            $"Melee-in-range stimulus: Engage↔Attack. FlockBehavior={fb}, GoalType={gt}");
    }

    // ── The parity fix that motivated this suite ──

    [Test]
    public void FlockOnCooldown_BothResolveFlank()
    {
        // This is the case that was silently diverging before the fix:
        //   GoalPriorityResolver returns Flank (cooldownReady=false, flockCount>=3)
        //   FlockStateResolver previously returned Engaging (no active wave) ← BUG
        //   FlockStateResolver now returns Flanking (flockCooldownActive=true) ← FIXED
        var fb = FlockStateResolver.Resolve(
            healthPercent: 0.8f, hasTarget: true, playerNearby: true, playerDist: 6f,
            isRanged: false, attackSlotsSaturated: false, meleeAttackActive: false,
            rangedAttackActive: false, maxDistanceFromCentroid: 3f, boidCount: 5,
            clusteredEnoughToEngage: true, flockCooldownActive: true);
        var gt = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 6f, isIsolated: false, flockCount: 5);

        Assert.IsTrue(IsEquivalent(fb, gt, isRanged: false),
            $"On-cooldown stimulus should flank in both. FlockBehavior={fb}, GoalType={gt}");
    }

    [Test]
    public void SlotsSaturated_BothResolveFlank()
    {
        var fb = FlockStateResolver.Resolve(
            healthPercent: 0.8f, hasTarget: true, playerNearby: true, playerDist: 6f,
            isRanged: false, attackSlotsSaturated: true, meleeAttackActive: false,
            rangedAttackActive: false, maxDistanceFromCentroid: 3f, boidCount: 5,
            clusteredEnoughToEngage: true);
        var gt = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 6f, isIsolated: false, flockCount: 5,
            attackSlotAvailable: false);

        Assert.IsTrue(IsEquivalent(fb, gt, isRanged: false),
            $"Slot-saturated stimulus should flank in both. FlockBehavior={fb}, GoalType={gt}");
    }

    // ── Regrouping parity ──

    [Test]
    public void SpreadFlock_NoPlayer_BothResolveRegroup()
    {
        // FlockStateResolver uses max-distance-from-centroid; GoalPriorityResolver
        // uses per-agent isIsolated. Driven by identical isolationThreshold.
        var fb = FlockStateResolver.Resolve(
            healthPercent: 0.8f, hasTarget: true, playerNearby: false, playerDist: 50f,
            isRanged: false, attackSlotsSaturated: false, meleeAttackActive: false,
            rangedAttackActive: false, maxDistanceFromCentroid: 25f, boidCount: 5,
            clusteredEnoughToEngage: false);
        var gt = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 50f, isIsolated: true, flockCount: 5);

        Assert.IsTrue(IsEquivalent(fb, gt, isRanged: false),
            $"Spread-flock stimulus should regroup in both. FlockBehavior={fb}, GoalType={gt}");
    }

    // ── Guard parity ──

    [Test]
    public void MidRange_NoEngagement_BothResolveGuard()
    {
        var fb = FlockStateResolver.Resolve(
            healthPercent: 0.8f, hasTarget: true, playerNearby: false, playerDist: 22f,
            isRanged: false, attackSlotsSaturated: false, meleeAttackActive: false,
            rangedAttackActive: false, maxDistanceFromCentroid: 3f, boidCount: 3,
            clusteredEnoughToEngage: false);
        var gt = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 22f, isIsolated: false, flockCount: 3);

        Assert.IsTrue(IsEquivalent(fb, gt, isRanged: false),
            $"Mid-range stimulus should guard in both. FlockBehavior={fb}, GoalType={gt}");
    }

    // ── Priority ordering parity: Scatter beats everything ──

    [Test]
    public void CriticalHealth_BeatsFlockCooldown_InBothResolvers()
    {
        var fb = FlockStateResolver.Resolve(
            healthPercent: 0.10f, hasTarget: true, playerNearby: true, playerDist: 6f,
            isRanged: false, attackSlotsSaturated: true, meleeAttackActive: false,
            rangedAttackActive: false, maxDistanceFromCentroid: 3f, boidCount: 5,
            clusteredEnoughToEngage: true, flockCooldownActive: true);
        var gt = ResolveGOAPBoidGoal(
            healthPercent: 0.10f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 6f, isIsolated: false, flockCount: 5,
            attackSlotAvailable: false);

        Assert.AreEqual(FlockBehavior.Scattering, fb);
        Assert.AreEqual(GoalType.Scatter, gt);
    }

    // ── Flank-gate parity: tiny flock should NOT flank in either ──

    [Test]
    public void TinyFlock_OnCooldown_DoesNotFlank_InEitherResolver()
    {
        var fb = FlockStateResolver.Resolve(
            healthPercent: 0.8f, hasTarget: true, playerNearby: true, playerDist: 6f,
            isRanged: false, attackSlotsSaturated: false, meleeAttackActive: false,
            rangedAttackActive: false, maxDistanceFromCentroid: 3f, boidCount: 2,
            clusteredEnoughToEngage: true, flockCooldownActive: true);
        var gt = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 6f, isIsolated: false, flockCount: 2);

        Assert.AreNotEqual(FlockBehavior.Flanking, fb, "2-boid flock should not flank");
        Assert.AreNotEqual(GoalType.Flank, gt, "2-boid flock should not flank");
    }
}
