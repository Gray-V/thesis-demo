using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class PureRangedCombatCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("PureRangedCombatCapability");

        builder.AddGoal<PureRangedAttackGoal>()
            .SetBaseCost(1)
            .AddCondition<PureRangedAttackDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<PureRangedAttackAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<PlayerVisible>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<PureRangedAttackDone>(EffectType.Increase);

        // PlayerVisionSensor is already registered by PureCombatCapabilityFactory — don't duplicate

        return builder.Build();
    }
}
