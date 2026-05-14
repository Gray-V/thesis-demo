using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class GOAPBoidKiteCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("GOAPBoidKiteCapability");

        builder.AddGoal<KiteGoal>()
            .SetBaseCost(1)
            .AddCondition<KiteDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<GOAPBoidKiteAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<IsRangedType>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<KiteDone>(EffectType.Increase);

        builder.AddWorldSensor<GOAPBoidRangedTypeSensor>()
            .SetKey<IsRangedType>();

        return builder.Build();
    }
}
