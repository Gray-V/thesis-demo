using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Wander capability: agents move as a school toward shared wander targets.
/// Single action — PureSwarmMoveAction handles both cohesion and target-seeking.
/// </summary>
public class PureWanderCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("PureWanderCapability");

        // Goal: Wander as a school
        builder.AddGoal<PureWanderGoal>()
            .SetBaseCost(10)
            .AddCondition<IsWandering>(Comparison.GreaterThanOrEqual, 1);

        // Single action: swarm movement toward shared target
        builder.AddAction<PureSwarmMoveAction>()
            .SetBaseCost(1)
            .SetTarget<WanderTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddEffect<IsWandering>(EffectType.Increase);

        // Sensor: shared wander target
        builder.AddTargetSensor<WanderTargetSensor>()
            .SetTarget<WanderTarget>();

        return builder.Build();
    }
}
