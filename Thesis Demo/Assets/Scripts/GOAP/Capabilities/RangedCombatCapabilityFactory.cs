using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Capability for ranged boids.
/// Goal: RangedAttackGoal (cost 1, condition AttackComplete >= 1).
/// Plan: CircleAction → FireAction.
/// Sensors: range, slot, cooldown, circle state.
/// </summary>
public class RangedCombatCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("RangedCombat");

        builder.AddGoal<RangedAttackGoal>()
            .SetBaseCost(1)
            .AddCondition<RangedAttackDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<CircleAction>()
            .SetBaseCost(1)
            .AddCondition<IsInAttackRange>(Comparison.GreaterThanOrEqual, 1)
            .AddCondition<AttackSlotFree>(Comparison.GreaterThanOrEqual, 1)
            .AddCondition<IsOnCooldown>(Comparison.SmallerThanOrEqual, 0)
            .AddEffect<CircleDone>(EffectType.Increase)
            .SetRequiresTarget(false);

        builder.AddAction<FireAction>()
            .SetBaseCost(1)
            .AddCondition<CircleDone>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<RangedAttackDone>(EffectType.Increase)
            .SetRequiresTarget(false);

        builder.AddWorldSensor<AttackRangeSensor>()
            .SetKey<IsInAttackRange>();

        builder.AddWorldSensor<AttackSlotSensor>()
            .SetKey<AttackSlotFree>();

        builder.AddWorldSensor<CooldownSensor>()
            .SetKey<IsOnCooldown>();

        builder.AddWorldSensor<CircleDoneSensor>()
            .SetKey<CircleDone>();

        return builder.Build();
    }
}
