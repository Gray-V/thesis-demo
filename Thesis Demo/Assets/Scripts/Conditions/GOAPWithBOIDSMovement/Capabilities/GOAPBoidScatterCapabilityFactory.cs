using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class GOAPBoidScatterCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("GOAPBoidScatterCapability");

        builder.AddGoal<ScatterGoal>()
            .SetBaseCost(0)
            .AddCondition<ScatterDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<GOAPBoidScatterAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<IsHealthCritical>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<ScatterDone>(EffectType.Increase);

        builder.AddWorldSensor<GOAPBoidCriticalHealthSensor>()
            .SetKey<IsHealthCritical>();

        return builder.Build();
    }
}
