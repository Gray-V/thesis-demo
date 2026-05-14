using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class LeaderFlankCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("LeaderFlankCapability");

        builder.AddGoal<LeaderFlankGoal>()
            .SetBaseCost(4)
            .AddCondition<LeaderFlankDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<LeaderFlankAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<LeaderMultipleAgentsAlive>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<LeaderFlankDone>(EffectType.Increase);

        builder.AddWorldSensor<LeaderFlockSizeSensor>()
            .SetKey<LeaderMultipleAgentsAlive>();

        return builder.Build();
    }
}
