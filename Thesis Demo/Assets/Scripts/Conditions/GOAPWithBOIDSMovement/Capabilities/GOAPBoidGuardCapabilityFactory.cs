using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class GOAPBoidGuardCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("GOAPBoidGuardCapability");

        builder.AddGoal<GuardGoal>()
            .SetBaseCost(5)
            .AddCondition<GuardDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<GOAPBoidGuardAction>()
            .SetBaseCost(1)
            .SetTarget<GuardPointTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<PlayerInMidRange>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<GuardDone>(EffectType.Increase);

        builder.AddWorldSensor<GOAPBoidMidRangeSensor>()
            .SetKey<PlayerInMidRange>();

        builder.AddTargetSensor<GOAPBoidGuardPointSensor>()
            .SetTarget<GuardPointTarget>();

        return builder.Build();
    }
}
