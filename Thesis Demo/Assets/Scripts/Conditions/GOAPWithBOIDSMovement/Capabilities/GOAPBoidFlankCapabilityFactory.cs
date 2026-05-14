using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

public class GOAPBoidFlankCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("GOAPBoidFlankCapability");

        builder.AddGoal<FlankGoal>()
            .SetBaseCost(4)
            .AddCondition<FlankDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<GOAPBoidFlankAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<MultipleAgentsAlive>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<FlankDone>(EffectType.Increase);

        builder.AddWorldSensor<GOAPBoidFlockSizeSensor>()
            .SetKey<MultipleAgentsAlive>();

        return builder.Build();
    }
}
