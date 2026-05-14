using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class GOAPBoidFleeCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("GOAPBoidFleeCapability");

        builder.AddGoal<PureFleeGoal>()
            .SetBaseCost(0)
            .AddCondition<FleeDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<GOAPBoidFleeAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<IsHealthLow>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<FleeDone>(EffectType.Increase);

        builder.AddWorldSensor<HealthLowSensor>()
            .SetKey<IsHealthLow>();

        return builder.Build();
    }
}
