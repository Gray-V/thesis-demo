using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Melee wind-up: boid hovers in place while pulling back.
/// On completion, locks the charge direction toward the player's current position.
/// Conditions: IsInAttackRange, AttackSlotFree, !IsOnCooldown.
/// Effect: WindUpDone.
/// </summary>
public class WindUpAction : GoapActionBase<WindUpAction.Data>
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
        data.Brain.isGoapAttacking = true;
        data.Brain.isMovementOverridden = true;
        data.Brain.windUpComplete = false;
        data.Brain.windUpTimer = data.Boid.settings.attackWindUpDuration;
        data.Brain.gotAttackSlot = data.Boid.manager.RequestAttack();
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        data.Boid.velocity = Vector3.zero;
        data.Brain.windUpTimer -= context.DeltaTime;
        return data.Brain.windUpTimer <= 0f ? ActionRunState.Completed : ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data)
    {
        data.Brain.windUpComplete = true;
        data.Brain.chargeDirection = data.Boid.manager.Target != null
            ? (data.Boid.manager.Target.position - data.Boid.Position).normalized
            : data.Boid.transform.forward;
    }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data)
    {
        // If interrupted (windUpComplete is false), ChargeAction won't run — release the slot now.
        if (!data.Brain.windUpComplete && data.Brain.gotAttackSlot)
        {
            data.Boid.manager.ReleaseAttack();
            data.Brain.gotAttackSlot = false;
        }

        // Only reset movement override if we were interrupted.
        // On normal Complete(), isMovementOverridden stays true for ChargeAction.
        if (!data.Brain.windUpComplete)
        {
            data.Brain.isGoapAttacking = false;
            data.Brain.isMovementOverridden = false;
        }
    }
}
