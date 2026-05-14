using NUnit.Framework;
using static GoalPriorityResolver;

/// <summary>
/// Locks down the <see cref="LeaderGoapBrain.ResolveGoalRequestType"/> table —
/// the pure mapping from <c>GoalPriorityResolver.GoalType</c> to the
/// <c>GoalBase</c> subclass requested by the leader's GOAP provider.
///
/// Why this fixture exists: on 2026-05-05 the four-bucket diagnostic batch
/// revealed that <see cref="LeaderGoapBrain"/>'s goal-request switch had no
/// case for <c>GoalType.RangedAttack</c>. The resolver's flips to RangedAttack
/// silently fell through to <c>default → LeaderWanderGoal</c>, leaving the
/// ranged leader Wandering through every v1 trial while the metric logged it
/// as engaged. This invalidated the v1 batch's ranged-flock combat metrics.
/// These tests stop that class of bug from recurring: every member of the
/// GoalType enum must map to its corresponding <c>Leader…Goal</c>, with one
/// explicit exception (Wander → LeaderWanderGoal, the safe fallback).
///
/// The fixture verifies the static helper rather than the production switch
/// in <see cref="LeaderGoapBrain.Update"/> directly because the switch
/// invokes the generic <c>provider.RequestGoal&lt;T&gt;()</c>, and
/// <c>GoapActionProvider</c> lives in a CrashKonijn assembly that the
/// EditMode test asmdef cannot reference. The helper MUST stay byte-identical
/// to the switch (per the comment on <c>ResolveGoalRequestType</c>) — drift
/// is what produced the original bug.
/// </summary>
[TestFixture]
public class LeaderGoalRequestMappingTests
{
    [Test]
    public void Scatter_MapsTo_LeaderScatterGoal()
    {
        Assert.AreEqual(typeof(LeaderScatterGoal),
            LeaderGoapBrain.ResolveGoalRequestType(GoalType.Scatter));
    }

    [Test]
    public void Flee_MapsTo_LeaderFleeGoal()
    {
        Assert.AreEqual(typeof(LeaderFleeGoal),
            LeaderGoapBrain.ResolveGoalRequestType(GoalType.Flee));
    }

    [Test]
    public void Attack_MapsTo_LeaderAttackGoal()
    {
        Assert.AreEqual(typeof(LeaderAttackGoal),
            LeaderGoapBrain.ResolveGoalRequestType(GoalType.Attack));
    }

    [Test]
    public void RangedAttack_MapsTo_LeaderRangedAttackGoal()
    {
        // The bug. Pre-fix: RangedAttack fell through to LeaderWanderGoal.
        // Post-fix: a dedicated LeaderRangedAttackGoal/Action handles it.
        // If this test ever fails, the ranged leader is silently downgraded
        // to Wander again and any v2 batch's ranged combat metrics are
        // contaminated the same way v1's were.
        Assert.AreEqual(typeof(LeaderRangedAttackGoal),
            LeaderGoapBrain.ResolveGoalRequestType(GoalType.RangedAttack),
            "GoalType.RangedAttack MUST map to LeaderRangedAttackGoal — "
            + "if it falls through to LeaderWanderGoal, the ranged leader is "
            + "silently broken (v1 bug, see wiki/experiments/v1-batch-2026-05-05.md).");
    }

    [Test]
    public void Kite_MapsTo_LeaderKiteGoal()
    {
        Assert.AreEqual(typeof(LeaderKiteGoal),
            LeaderGoapBrain.ResolveGoalRequestType(GoalType.Kite));
    }

    [Test]
    public void Regroup_MapsTo_LeaderRegroupGoal()
    {
        Assert.AreEqual(typeof(LeaderRegroupGoal),
            LeaderGoapBrain.ResolveGoalRequestType(GoalType.Regroup));
    }

    [Test]
    public void Flank_MapsTo_LeaderFlankGoal()
    {
        Assert.AreEqual(typeof(LeaderFlankGoal),
            LeaderGoapBrain.ResolveGoalRequestType(GoalType.Flank));
    }

    [Test]
    public void Guard_MapsTo_LeaderGuardGoal()
    {
        Assert.AreEqual(typeof(LeaderGuardGoal),
            LeaderGoapBrain.ResolveGoalRequestType(GoalType.Guard));
    }

    [Test]
    public void Wander_MapsTo_LeaderWanderGoal()
    {
        Assert.AreEqual(typeof(LeaderWanderGoal),
            LeaderGoapBrain.ResolveGoalRequestType(GoalType.Wander));
    }

    /// <summary>
    /// Exhaustive enum coverage — if a future commit adds a new GoalType
    /// without updating <see cref="LeaderGoapBrain.ResolveGoalRequestType"/>,
    /// the new value falls through to the default branch (LeaderWanderGoal)
    /// and is silently broken the same way RangedAttack was. This test
    /// iterates every enum value, asserts the helper returns SOMETHING, and
    /// (more importantly) that everything except the explicit Wander case
    /// returns a non-Wander goal. Catches the next "missing case" bug at
    /// CI time rather than next-batch time.
    /// </summary>
    [Test]
    public void EveryGoalTypeExceptWander_MapsToNonWanderGoal()
    {
        foreach (GoalType g in System.Enum.GetValues(typeof(GoalType)))
        {
            System.Type mapped = LeaderGoapBrain.ResolveGoalRequestType(g);
            Assert.IsNotNull(mapped, $"GoalType.{g} returned null mapping.");

            if (g == GoalType.Wander)
            {
                Assert.AreEqual(typeof(LeaderWanderGoal), mapped,
                    "Wander itself should map to LeaderWanderGoal.");
            }
            else
            {
                Assert.AreNotEqual(typeof(LeaderWanderGoal), mapped,
                    $"GoalType.{g} silently falls through to LeaderWanderGoal — "
                    + "add a dedicated case in LeaderGoapBrain.ResolveGoalRequestType "
                    + "AND in the parallel switch inside Update().");
            }
        }
    }
}
