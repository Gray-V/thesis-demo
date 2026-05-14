using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Melee charge: boid dashes along the locked direction and deals damage on contact.
/// Releases the attack slot and starts per-boid cooldown on completion.
/// Condition: WindUpDone.
/// Effect: AttackComplete.
/// </summary>
public class ChargeAction : GoapActionBase<ChargeAction.Data>
{
    public class Data : IActionData
    {
        public ITarget Target { get; set; }

        [GetComponent] public BoidAgent Boid { get; set; }
        [GetComponent] public BoidGoapBrain Brain { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.Brain.chargeTimer = data.Boid.settings.attackChargeDuration;
        data.Brain.damageDealtThisCharge = false;
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        data.Boid.velocity = data.Brain.chargeDirection * data.Boid.settings.attackChargeSpeed;

        if (!data.Brain.damageDealtThisCharge && data.Boid.manager.Target != null)
        {
            float dist = Vector3.Distance(data.Boid.manager.Target.position, data.Boid.Position);
            if (dist <= data.Boid.settings.attackContactDistance)
            {
                data.Boid.manager.Target.GetComponent<PlayerHealth>()?.TakeDamage(data.Boid.settings.attackDamage);
                BehavioralMetricsCollector.Instance?.LogEvent(
                    "AttackHit", data.Boid.gameObject.name, -1,
                    $"BoidCharge,Dmg={data.Boid.settings.attackDamage}");
                data.Brain.damageDealtThisCharge = true;
            }
        }

        data.Brain.chargeTimer -= context.DeltaTime;
        return data.Brain.chargeTimer <= 0f ? ActionRunState.Completed : ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data)
    {
        data.Brain.cooldownTimer = data.Boid.settings.attackCooldown;
    }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data)
    {
        // Release slot and reset all flags in both Complete and Stop paths.
        if (data.Brain.gotAttackSlot)
        {
            data.Boid.manager.ReleaseAttack();
            data.Brain.gotAttackSlot = false;
        }
        data.Brain.windUpComplete = false;
        data.Brain.isGoapAttacking = false;
        data.Brain.isMovementOverridden = false;
    }
}
