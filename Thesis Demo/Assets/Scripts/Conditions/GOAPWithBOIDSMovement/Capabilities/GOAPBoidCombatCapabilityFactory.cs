using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class GOAPBoidCombatCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("GOAPBoidCombatCapability");

        builder.AddGoal<PureAttackGoal>()
            .SetBaseCost(1)
            .AddCondition<PureAttackDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<GOAPBoidAttackAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<PlayerVisible>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<PureAttackDone>(EffectType.Increase);

        builder.AddMultiSensor<PlayerVisionSensor>();

        return builder.Build();
    }
}
