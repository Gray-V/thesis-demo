using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class LeaderScatterCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("LeaderScatterCapability");

        builder.AddGoal<LeaderScatterGoal>()
            .SetBaseCost(0)
            .AddCondition<LeaderScatterDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<LeaderScatterAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<LeaderIsHealthCritical>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<LeaderScatterDone>(EffectType.Increase);

        builder.AddWorldSensor<LeaderCriticalHealthSensor>()
            .SetKey<LeaderIsHealthCritical>();

        return builder.Build();
    }
}
