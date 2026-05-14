using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class GOAPBoidRangedCombatCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("GOAPBoidRangedCombatCapability");

        builder.AddGoal<PureRangedAttackGoal>()
            .SetBaseCost(1)
            .AddCondition<PureRangedAttackDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<GOAPBoidRangedAttackAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<PlayerVisible>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<PureRangedAttackDone>(EffectType.Increase);

        // PlayerVisionSensor is already registered by GOAPBoidCombatCapabilityFactory — don't duplicate

        return builder.Build();
    }
}
