using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Combat capability for PureGOAP agents.
/// Provides attack behavior when player is visible.
/// </summary>
public class PureCombatCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("PureCombatCapability");

        // Goal: Attack player
        builder.AddGoal<PureAttackGoal>()
            .SetBaseCost(1)
            .AddCondition<PureAttackDone>(Comparison.GreaterThanOrEqual, 1);

        // Action: Attack player
        builder.AddAction<PureAttackAction>()
            .SetBaseCost(1)
            .SetTarget<PlayerTarget>()
            .SetMoveMode(ActionMoveMode.PerformWhileMoving)
            .AddCondition<PlayerVisible>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<PureAttackDone>(EffectType.Increase);

        // Sensor: Detect player
        builder.AddMultiSensor<PlayerVisionSensor>();

        return builder.Build();
    }
}
