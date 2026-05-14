using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class GOAPBoidRegroupCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("GOAPBoidRegroupCapability");

        builder.AddGoal<RegroupGoal>()
            .SetBaseCost(3)
            .AddCondition<RegroupDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<GOAPBoidRegroupAction>()
            .SetBaseCost(1)
            .SetTarget<SwarmCentroidTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<IsIsolated>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<RegroupDone>(EffectType.Increase);

        builder.AddWorldSensor<GOAPBoidIsolationSensor>()
            .SetKey<IsIsolated>();

        builder.AddTargetSensor<GOAPBoidCentroidTargetSensor>()
            .SetTarget<SwarmCentroidTarget>();

        return builder.Build();
    }
}
