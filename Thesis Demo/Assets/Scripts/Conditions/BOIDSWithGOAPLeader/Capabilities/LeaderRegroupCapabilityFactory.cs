using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class LeaderRegroupCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("LeaderRegroupCapability");

        builder.AddGoal<LeaderRegroupGoal>()
            .SetBaseCost(3)
            .AddCondition<LeaderRegroupDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<LeaderRegroupAction>()
            .SetBaseCost(1)
            .SetTarget<LeaderFlockCentroidTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<LeaderIsIsolated>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<LeaderRegroupDone>(EffectType.Increase);

        builder.AddWorldSensor<LeaderIsolationSensor>()
            .SetKey<LeaderIsIsolated>();

        builder.AddTargetSensor<LeaderCentroidTargetSensor>()
            .SetTarget<LeaderFlockCentroidTarget>();

        return builder.Build();
    }
}
