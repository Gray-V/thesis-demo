using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static GoalPriorityResolver;

/// <summary>
/// Tests that the GoalPriorityResolver returns the correct goal
/// under simulated game conditions. These are the most thesis-relevant tests.
/// </summary>
[TestFixture]
public class BehaviorTransitionTests
{
    // These tests exercise GoalPriorityResolver with realistic parameter combos.
    // They don't require scene setup — the resolver is a pure function.
    // Placed in PlayMode because future versions may test full agent behavior.

    [Test]
    public void Agent_AttacksWhenPlayerNearby()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Attack, goal);
    }

    [Test]
    public void Agent_FleesWhenHealthLow()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.25f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Flee, goal);
    }

    [Test]
    public void Agent_ScattersWhenHealthCritical()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.10f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Scatter, goal);
    }

    [Test]
    public void Agent_RegroupsWhenIsolated()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 50f, isIsolated: true, flockCount: 5);

        Assert.AreEqual(GoalType.Regroup, goal);
    }

    [Test]
    public void Agent_GuardsWhenPlayerMidRange()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 20f, isIsolated: false, flockCount: 1);

        Assert.AreEqual(GoalType.Guard, goal);
    }

    [Test]
    public void Agent_FlanksWhenCooldownActive()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 10f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Flank, goal);
    }

    [Test]
    public void RangedAgent_KitesWhenPlayerClose()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: true,
            isRanged: true, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Kite, goal);
    }

    [Test]
    public void RangedAgent_RangedAttackWhenFarEnough()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: true,
            isRanged: true, playerDist: 12f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.RangedAttack, goal);
    }

    [Test]
    public void WandersWhenNoConditionsMet()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 50f, isIsolated: false, flockCount: 1);

        Assert.AreEqual(GoalType.Wander, goal);
    }

    // ── Full priority chain ordering ──

    [Test]
    public void PriorityChain_ScatterFleeAttackRegroupFlankGuardWander()
    {
        // Scatter: critical health + player
        Assert.AreEqual(GoalType.Scatter,
            ResolveGOAPBoidGoal(0.05f, true, true, false, 5f, true, 5));

        // Flee: low health (not critical) + player
        Assert.AreEqual(GoalType.Flee,
            ResolveGOAPBoidGoal(0.20f, true, true, false, 5f, false, 5));

        // Attack: healthy + player + cooldown ready
        Assert.AreEqual(GoalType.Attack,
            ResolveGOAPBoidGoal(0.80f, true, true, false, 5f, false, 5));

        // Regroup: isolated, no player
        Assert.AreEqual(GoalType.Regroup,
            ResolveGOAPBoidGoal(0.80f, false, false, false, 50f, true, 5));

        // Flank: player nearby, cooldown NOT ready, 3+ agents
        Assert.AreEqual(GoalType.Flank,
            ResolveGOAPBoidGoal(0.80f, true, false, false, 10f, false, 5));

        // Guard: player at mid range
        Assert.AreEqual(GoalType.Guard,
            ResolveGOAPBoidGoal(0.80f, false, false, false, 20f, false, 1));

        // Wander: nothing else
        Assert.AreEqual(GoalType.Wander,
            ResolveGOAPBoidGoal(0.80f, false, false, false, 50f, false, 1));
    }
}
