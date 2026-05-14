using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class LeaderGuardCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("LeaderGuardCapability");

        builder.AddGoal<LeaderGuardGoal>()
            .SetBaseCost(5)
            .AddCondition<LeaderGuardDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<LeaderGuardAction>()
            .SetBaseCost(1)
            .SetTarget<LeaderGuardPointTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<LeaderPlayerInMidRange>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<LeaderGuardDone>(EffectType.Increase);

        builder.AddWorldSensor<LeaderMidRangeSensor>()
            .SetKey<LeaderPlayerInMidRange>();

        builder.AddTargetSensor<LeaderGuardPointSensor>()
            .SetTarget<LeaderGuardPointTarget>();

        return builder.Build();
    }
}
