using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Shared capability used by both melee and ranged agent types.
/// Provides FlockGoal (cost 10) + FlockAction so every boid can fall back to normal flocking.
/// </summary>
public class FlockCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("Flock");

        builder.AddGoal<FlockGoal>()
            .SetBaseCost(10)
            .AddCondition<IsFlocking>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<FlockAction>()
            .AddEffect<IsFlocking>(EffectType.Increase)
            .SetRequiresTarget(false);

        return builder.Build();
    }
}
