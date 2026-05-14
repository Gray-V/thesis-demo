using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Kite action for GOAPWithBOIDSMovement: backpedal while firing.
/// BOIDS separation naturally spreads kiting agents apart.
/// Two phases: Retreat → Fire.
/// </summary>
public class GOAPBoidKiteAction : GoapActionBase<GOAPBoidKiteAction.Data>
{
    private const float KiteMinDistance = 8f;
    private const float KiteMaxDistance = 15f;
    private const float ProjectileSpeed = 15f;
    private const float ProjectileDamage = 30f;
    private const float ProjectileTurnSpeed = 5f;

    public enum Phase { Retreat, Fire }

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public GOAPBoidAgent Agent { get; set; }
        public Phase CurrentPhase { get; set; }
        public float KiteTimer { get; set; }
        public bool SlotAcquired { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.CurrentPhase = Phase.Retreat;
        data.KiteTimer = 8f;
        data.SlotAcquired = GOAPBoidAgent.RequestAttackSlot(data.Agent.flockId);

        if (data.Target is TransformTarget transformTarget)
            data.Agent.targetPlayer = transformTarget.Transform;

        if (data.SlotAcquired)
            GOAPBoidAgent.SetFlockCooldown(data.Agent.flockId, data.Agent.SwarmCooldownDuration);
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (!data.SlotAcquired || data.Target == null || data.Agent.targetPlayer == null)
            return ActionRunState.Stop;

        Vector3 agentPos = data.Agent.Position;
        Vector3 targetPos = data.Target.Position;
        float distance = Vector3.Distance(agentPos, targetPos);

        switch (data.CurrentPhase)
        {
            case Phase.Retreat:
                // Backpedal: steer away from player
                Vector3 retreatPoint = agentPos + (agentPos - targetPos).normalized * 20f;
                data.Agent.SteerToward(retreatPoint);

                // Transition to Fire when in range
                if (distance >= KiteMinDistance && distance <= KiteMaxDistance)
                    data.CurrentPhase = Phase.Fire;
                break;

            case Phase.Fire:
                if (data.Agent.ProjectilePrefab != null)
                {
                    Vector3 fireDir = (targetPos - agentPos).normalized;
                    GameObject proj = Object.Instantiate(
                        data.Agent.ProjectilePrefab,
                        agentPos,
                        Quaternion.LookRotation(fireDir)
                    );
                    BoidProjectile bp = proj.GetComponent<BoidProjectile>();
                    bp?.Initialize(fireDir, ProjectileSpeed, ProjectileDamage, data.Agent.targetPlayer, ProjectileTurnSpeed);
                    BehavioralMetricsCollector.Instance?.LogEvent(
                        "AttackFired", data.Agent.gameObject.name, data.Agent.flockId,
                        $"Kite,Dmg={ProjectileDamage}");
                }
                return ActionRunState.Completed;
        }

        data.KiteTimer -= context.DeltaTime;
        if (data.KiteTimer <= 0f)
            return ActionRunState.Completed;

        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data)
    {
        data.Agent.cooldownTimer = data.Agent.AttackCooldown;
        if (data.SlotAcquired)
        {
            GOAPBoidAgent.SetFlockCooldown(data.Agent.flockId, data.Agent.SwarmCooldownDuration);
            GOAPBoidAgent.ReleaseAttackSlot(data.Agent.flockId);
            data.SlotAcquired = false;
        }
    }

    public override void Stop(IMonoAgent agent, Data data)
    {
        if (data.SlotAcquired)
        {
            GOAPBoidAgent.ReleaseAttackSlot(data.Agent.flockId);
            data.SlotAcquired = false;
        }
    }

    public override void End(IMonoAgent agent, Data data)
    {
        if (data.SlotAcquired)
        {
            GOAPBoidAgent.ReleaseAttackSlot(data.Agent.flockId);
            data.SlotAcquired = false;
        }
        data.Agent.targetPlayer = null;
    }
}
