using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Flee capability for PureGOAP agents.
/// Highest priority (cost 0) — triggers when health drops below 30%.
/// Reuses PlayerTarget from PureCombatCapability for flee direction.
/// </summary>
public class PureFleeCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("PureFleeCapability");

        // Goal: Flee (highest priority)
        builder.AddGoal<PureFleeGoal>()
            .SetBaseCost(0)
            .AddCondition<FleeDone>(Comparison.GreaterThanOrEqual, 1);

        // Action: Flee from player
        builder.AddAction<PureFleeAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<IsHealthLow>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<FleeDone>(EffectType.Increase);

        // Sensor: Health check
        builder.AddWorldSensor<HealthLowSensor>()
            .SetKey<IsHealthLow>();

        return builder.Build();
    }
}
