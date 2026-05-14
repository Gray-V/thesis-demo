using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Ranged fire phase: instantiates a projectile aimed at the player and completes immediately.
/// Releases the attack slot and starts per-boid cooldown on completion.
/// Condition: CircleDone.
/// Effect: AttackComplete.
/// </summary>
public class FireAction : GoapActionBase<FireAction.Data>
{
    public class Data : IActionData
    {
        public ITarget Target { get; set; }

        [GetComponent] public BoidAgent Boid { get; set; }
        [GetComponent] public BoidGoapBrain Brain { get; set; }
    }

    public override void Created() { }
    public override void Start(IMonoAgent agent, Data data) { }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        var settings = data.Boid.settings;
        var target = data.Boid.manager.Target;

        if (settings.projectilePrefab != null && target != null)
        {
            Vector3 dir = (target.position - data.Boid.Position).normalized;
            GameObject proj = Object.Instantiate(
                settings.projectilePrefab,
                data.Boid.Position,
                Quaternion.LookRotation(dir));

            BoidProjectile bp = proj.GetComponent<BoidProjectile>();
            bp?.Initialize(dir, settings.projectileSpeed, settings.attackDamage, target, settings.projectileTurnSpeed);
        }

        return ActionRunState.Completed;
    }

    public override void Complete(IMonoAgent agent, Data data)
    {
        data.Brain.cooldownTimer = data.Boid.settings.attackCooldown;
    }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data)
    {
        if (data.Brain.gotAttackSlot)
        {
            data.Boid.manager.ReleaseAttack();
            data.Brain.gotAttackSlot = false;
        }
        data.Brain.circleDone = false;
        data.Brain.isGoapAttacking = false;
        data.Brain.isMovementOverridden = false;
    }
}
