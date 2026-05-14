using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class LeaderFleeCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("LeaderFleeCapability");

        builder.AddGoal<LeaderFleeGoal>()
            .SetBaseCost(0)
            .AddCondition<LeaderFleeDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<LeaderFleeAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<LeaderIsHealthLow>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<LeaderFleeDone>(EffectType.Increase);

        builder.AddWorldSensor<LeaderHealthSensor>()
            .SetKey<LeaderIsHealthLow>();

        return builder.Build();
    }
}
