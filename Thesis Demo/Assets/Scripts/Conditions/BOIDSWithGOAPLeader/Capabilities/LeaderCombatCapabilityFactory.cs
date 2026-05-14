using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class LeaderCombatCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("LeaderCombatCapability");

        builder.AddGoal<LeaderAttackGoal>()
            .SetBaseCost(1)
            .AddCondition<LeaderAttackDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<LeaderAttackAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<PlayerVisible>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<LeaderAttackDone>(EffectType.Increase);

        builder.AddMultiSensor<PlayerVisionSensor>();

        return builder.Build();
    }
}
