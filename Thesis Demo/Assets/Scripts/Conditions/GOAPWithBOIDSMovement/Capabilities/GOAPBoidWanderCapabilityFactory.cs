using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class GOAPBoidWanderCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("GOAPBoidWanderCapability");

        builder.AddGoal<PureWanderGoal>()
            .SetBaseCost(10)
            .AddCondition<IsWandering>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<GOAPBoidWanderAction>()
            .SetBaseCost(1)
            .SetTarget<WanderTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddEffect<IsWandering>(EffectType.Increase);

        builder.AddTargetSensor<GOAPBoidWanderTargetSensor>()
            .SetTarget<WanderTarget>();

        return builder.Build();
    }
}
