using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Capability for melee boids.
/// Goal: MeleeAttackGoal (cost 1, condition AttackComplete >= 1).
/// Plan: WindUpAction → ChargeAction.
/// Sensors: range, slot, cooldown, wind-up state.
/// </summary>
public class MeleeCombatCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("MeleeCombat");

        builder.AddGoal<MeleeAttackGoal>()
            .SetBaseCost(1)
            .AddCondition<MeleeAttackDone>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<WindUpAction>()
            .SetBaseCost(1)
            .AddCondition<IsInAttackRange>(Comparison.GreaterThanOrEqual, 1)
            .AddCondition<AttackSlotFree>(Comparison.GreaterThanOrEqual, 1)
            .AddCondition<IsOnCooldown>(Comparison.SmallerThanOrEqual, 0)
            .AddEffect<WindUpDone>(EffectType.Increase)
            .SetRequiresTarget(false);

        builder.AddAction<ChargeAction>()
            .SetBaseCost(1)
            .AddCondition<WindUpDone>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<MeleeAttackDone>(EffectType.Increase)
            .SetRequiresTarget(false);

        builder.AddWorldSensor<AttackRangeSensor>()
            .SetKey<IsInAttackRange>();

        builder.AddWorldSensor<AttackSlotSensor>()
            .SetKey<AttackSlotFree>();

        builder.AddWorldSensor<CooldownSensor>()
            .SetKey<IsOnCooldown>();

        builder.AddWorldSensor<WindUpDoneSensor>()
            .SetKey<WindUpDone>();

        return builder.Build();
    }
}
