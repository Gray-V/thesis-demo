using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Capability config for <see cref="LeaderRangedAttackGoal"/> /
/// <see cref="LeaderRangedAttackAction"/>. Mirrors
/// <see cref="LeaderKiteCapabilityFactory"/> with the same
/// <see cref="LeaderIsRangedType"/> precondition so only ranged-flock leaders
/// can plan this action. The <see cref="LeaderRangedTypeSensor"/> world key
/// is registered by <see cref="LeaderKiteCapabilityFactory"/>; both
/// capabilities share that key, so we don't re-register it here.
/// </summary>
public class LeaderRangedAttackCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("LeaderRangedAttackCapability");

        builder.AddGoal<LeaderRangedAttackGoal>()
            .SetBaseCost(1)
            .AddCondition<LeaderRangedAttackDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<LeaderRangedAttackAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<LeaderIsRangedType>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<LeaderRangedAttackDone>(EffectType.Increase);

        return builder.Build();
    }
}
