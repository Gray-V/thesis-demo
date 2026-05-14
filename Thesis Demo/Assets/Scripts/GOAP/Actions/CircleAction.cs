using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Ranged orbit phase: boid circles the player at close range before firing.
/// Conditions: IsInAttackRange, AttackSlotFree, !IsOnCooldown.
/// Effect: CircleDone.
/// </summary>
public class CircleAction : GoapActionBase<CircleAction.Data>
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
        data.Brain.circleDone = false;
        data.Brain.gotAttackSlot = data.Boid.manager.RequestAttack();
        data.Brain.circleTimer = data.Boid.settings.attackWindUpDuration * 2f;

        // Seed angle from the boid's current position so the orbit starts smoothly.
        if (data.Boid.manager.Target != null)
        {
            Vector3 offset = data.Boid.Position - data.Boid.manager.Target.position;
            data.Brain.circleAngle = Mathf.Atan2(offset.z, offset.x);
        }
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (data.Boid.manager.Target == null)
            return ActionRunState.Completed;

        float orbitSpeed = data.Boid.settings.formationOrbitSpeed * Mathf.Deg2Rad;
        data.Brain.circleAngle += orbitSpeed * context.DeltaTime;

        float orbitRadius = data.Boid.settings.attackTriggerDistance * 0.8f;
        Vector3 center = data.Boid.manager.Target.position;

        Vector3 orbitPos = center + new Vector3(
            Mathf.Cos(data.Brain.circleAngle) * orbitRadius,
            0f,
            Mathf.Sin(data.Brain.circleAngle) * orbitRadius);

        Vector3 toOrbit = orbitPos - data.Boid.Position;
        data.Boid.velocity = toOrbit.normalized * data.Boid.settings.maxSpeed;

        Vector3 toTarget = center - data.Boid.Position;
        if (toTarget.sqrMagnitude > 0.01f)
            data.Boid.transform.forward = toTarget.normalized;

        data.Brain.circleTimer -= context.DeltaTime;
        return data.Brain.circleTimer <= 0f ? ActionRunState.Completed : ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data)
    {
        data.Brain.circleDone = true;
        // Keep isMovementOverridden = true so FireAction can hold position.
    }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data)
    {
        // If interrupted (circleDone is false), FireAction won't run — release slot now.
        if (!data.Brain.circleDone && data.Brain.gotAttackSlot)
        {
            data.Boid.manager.ReleaseAttack();
            data.Brain.gotAttackSlot = false;
        }

        if (!data.Brain.circleDone)
        {
            data.Brain.isGoapAttacking = false;
            data.Brain.isMovementOverridden = false;
        }
    }
}
