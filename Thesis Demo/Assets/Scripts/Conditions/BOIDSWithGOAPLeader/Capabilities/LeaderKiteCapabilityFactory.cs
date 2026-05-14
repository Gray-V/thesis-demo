using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class LeaderKiteCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("LeaderKiteCapability");

        builder.AddGoal<LeaderKiteGoal>()
            .SetBaseCost(1)
            .AddCondition<LeaderKiteDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<LeaderKiteAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<LeaderIsRangedType>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<LeaderKiteDone>(EffectType.Increase);

        builder.AddWorldSensor<LeaderRangedTypeSensor>()
            .SetKey<LeaderIsRangedType>();

        return builder.Build();
    }
}
