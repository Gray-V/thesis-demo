using NUnit.Framework;
using static GoalPriorityResolver;

/// <summary>
/// Locks down the <see cref="LeaderGoapBrain.MapGoalToFlockState"/> table —
/// the pure mapping from the leader's resolved <c>GoalType</c> to the
/// <see cref="FlockManager.FlockState"/> that the leader stamps onto its
/// flock manager every Update tick. Production callsite is the line right
/// after the leader's goal-request switch in <see cref="LeaderGoapBrain.Update"/>.
///
/// Why this fixture exists: the FSM-state coupling (added 2026-05-06) is the
/// mechanism that makes followers actually *do* the leader's named behaviors
/// (Scatter, Flank, Guard, Flee, Kite, Regroup) instead of merely cohering to
/// the leader. Same anti-drift rationale as
/// <see cref="LeaderGoalRequestMappingTests"/>: every member of <c>GoalType</c>
/// has a defensible <c>FlockState</c> mapping; a future refactor that drops a
/// case (the v1 RangedAttack class of bug) breaks the parity claim "all 9
/// behaviors expressed at flock level" silently.
/// </summary>
[TestFixture]
public class LeaderGoalToFlockStateMappingTests
{
    [Test]
    public void Scatter_MapsTo_Scattering()
    {
        Assert.AreEqual(FlockManager.FlockState.Scattering,
            LeaderGoapBrain.MapGoalToFlockState(GoalType.Scatter));
    }

    [Test]
    public void Flee_MapsTo_Fleeing()
    {
        Assert.AreEqual(FlockManager.FlockState.Fleeing,
            LeaderGoapBrain.MapGoalToFlockState(GoalType.Flee));
    }

    [Test]
    public void Attack_MapsTo_Engaging()
    {
        Assert.AreEqual(FlockManager.FlockState.Engaging,
            LeaderGoapBrain.MapGoalToFlockState(GoalType.Attack));
    }

    [Test]
    public void RangedAttack_MapsTo_Engaging()
    {
        // Same Engaging state as melee Attack: per-boid steering branches are
        // identical for the two; ranged-vs-melee divergence is handled by the
        // formation-ring system, not the per-state target-seek branch.
        Assert.AreEqual(FlockManager.FlockState.Engaging,
            LeaderGoapBrain.MapGoalToFlockState(GoalType.RangedAttack));
    }

    [Test]
    public void Kite_MapsTo_Kiting()
    {
        Assert.AreEqual(FlockManager.FlockState.Kiting,
            LeaderGoapBrain.MapGoalToFlockState(GoalType.Kite));
    }

    [Test]
    public void Regroup_MapsTo_Regrouping()
    {
        Assert.AreEqual(FlockManager.FlockState.Regrouping,
            LeaderGoapBrain.MapGoalToFlockState(GoalType.Regroup));
    }

    [Test]
    public void Flank_MapsTo_Flanking()
    {
        Assert.AreEqual(FlockManager.FlockState.Flanking,
            LeaderGoapBrain.MapGoalToFlockState(GoalType.Flank));
    }

    [Test]
    public void Guard_MapsTo_Guarding()
    {
        Assert.AreEqual(FlockManager.FlockState.Guarding,
            LeaderGoapBrain.MapGoalToFlockState(GoalType.Guard));
    }

    [Test]
    public void Wander_MapsTo_Idle()
    {
        // The intentional fallback: Wander is the leader's "no engagement" goal.
        // Idle disables per-state target-seek branches; followers just cohere.
        Assert.AreEqual(FlockManager.FlockState.Idle,
            LeaderGoapBrain.MapGoalToFlockState(GoalType.Wander));
    }

    [Test]
    public void EveryGoalType_MapsToANonGroupingFlockState()
    {
        // Grouping is a transient state set internally by FlockManager.SetTarget on
        // first target acquisition. The leader's mapping must never produce it —
        // the leader-driven path bypasses the SetTarget hand-off entirely and would
        // wedge the flock at "just acquired target" if it stamped Grouping.
        foreach (GoalType goal in System.Enum.GetValues(typeof(GoalType)))
        {
            FlockManager.FlockState mapped = LeaderGoapBrain.MapGoalToFlockState(goal);
            Assert.AreNotEqual(FlockManager.FlockState.Grouping, mapped,
                $"Leader goal {goal} must not map to FlockState.Grouping (which is reserved for SetTarget's transient hand-off).");
        }
    }
}
