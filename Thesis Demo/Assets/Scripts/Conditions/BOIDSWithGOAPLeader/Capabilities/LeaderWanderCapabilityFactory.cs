using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class LeaderWanderCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("LeaderWanderCapability");

        builder.AddGoal<LeaderWanderGoal>()
            .SetBaseCost(10)
            .AddCondition<LeaderWanderDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<LeaderWanderAction>()
            .SetBaseCost(1)
            .SetTarget<LeaderWanderTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddEffect<LeaderWanderDone>(EffectType.Increase);

        builder.AddTargetSensor<LeaderWanderTargetSensor>()
            .SetTarget<LeaderWanderTarget>();

        return builder.Build();
    }
}
