using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// 3-phase melee attack for GOAPWithBOIDSMovement: Approach → WindUp → Charge.
/// Same behavior as PureAttackAction but references GOAPBoidAgent.
/// </summary>
public class GOAPBoidAttackAction : GoapActionBase<GOAPBoidAttackAction.Data>
{
    private const float TriggerDistance = 6f;
    private const float WindUpDuration = 0.4f;
    private const float ChargeDuration = 0.6f;
    private const float ChargeSpeedMultiplier = 3f;
    private const float DamageContactDistance = 2f;

    public enum Phase { Approach, WindUp, Charge }

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public GOAPBoidAgent Agent { get; set; }
        public Phase CurrentPhase { get; set; }
        public float PhaseTimer { get; set; }
        public Vector3 ChargeDirection { get; set; }
        public bool DamageDealt { get; set; }
        public bool SlotAcquired { get; set; }
        // Direction from player toward this agent (XZ) — used to spread attackers in an arc
        public Vector3 PreferredApproachDir { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.CurrentPhase = Phase.Approach;
        data.PhaseTimer = 0f;
        data.DamageDealt = false;
        data.SlotAcquired = GOAPBoidAgent.RequestAttackSlot(data.Agent.flockId);

        if (data.Target is TransformTarget transformTarget)
            data.Agent.targetPlayer = transformTarget.Transform;

        // Hand the target to the flock manager so it can run its synchronized
        // melee wave in LateUpdate while this agent runs its own phases.
        if (data.Agent.flockManager != null && data.Agent.targetPlayer != null)
            data.Agent.flockManager.NotifyTargetAcquired(data.Agent.targetPlayer);

        // Compute natural approach direction from the player toward this agent (XZ only).
        // Stored at action-start so each attacker approaches from its own angle, preventing pile-up.
        if (data.Agent.targetPlayer != null)
        {
            Vector3 toAgent = data.Agent.Position - data.Agent.targetPlayer.position;
            toAgent.y = 0f;
            data.PreferredApproachDir = toAgent.sqrMagnitude > 0.001f ? toAgent.normalized : Vector3.forward;
        }
        else
        {
            data.PreferredApproachDir = Vector3.forward;
        }

        // Only set swarm cooldown when this agent actually got a slot
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

        // Flock-wide melee wave sync. When the manager enters Charging, every
        // in-flight melee attacker snaps to Charge so the surge lands together.
        var mgr = data.Agent.flockManager;
        if (mgr != null
            && data.CurrentPhase != Phase.Charge
            && mgr.CurrentMeleePhase == GOAPBoidFlockManager.MeleeAttackPhase.Charging)
        {
            data.ChargeDirection = (targetPos - agentPos).normalized;
            data.CurrentPhase = Phase.Charge;
            data.PhaseTimer = ChargeDuration;
        }

        switch (data.CurrentPhase)
        {
            case Phase.Approach:
                // Steer toward a point offset from the player in this agent's preferred direction.
                // This spreads simultaneous attackers in an arc rather than funneling to the same spot.
                data.Agent.SteerToward(targetPos + data.PreferredApproachDir * DamageContactDistance);
                if (distance <= TriggerDistance)
                {
                    data.CurrentPhase = Phase.WindUp;
                    data.PhaseTimer = WindUpDuration;
                    data.Agent.SetVelocity(data.Agent.velocity * 0.2f);
                }
                break;

            case Phase.WindUp:
                data.Agent.SetVelocity(Vector3.zero);
                data.PhaseTimer -= context.DeltaTime;
                if (data.PhaseTimer <= 0f)
                {
                    data.ChargeDirection = (targetPos - agentPos).normalized;
                    data.CurrentPhase = Phase.Charge;
                    data.PhaseTimer = ChargeDuration;
                }
                break;

            case Phase.Charge:
                float chargeSpeed = data.Agent.MaxSpeed * ChargeSpeedMultiplier;
                data.Agent.SetVelocity(data.ChargeDirection * chargeSpeed);
                data.PhaseTimer -= context.DeltaTime;

                if (!data.DamageDealt && distance <= DamageContactDistance)
                {
                    var playerHealth = data.Agent.targetPlayer.GetComponent<PlayerHealth>();
                    if (playerHealth != null)
                    {
                        playerHealth.TakeDamage(data.Agent.AttackDamage);
                        BehavioralMetricsCollector.Instance?.LogEvent(
                            "AttackHit", data.Agent.gameObject.name, data.Agent.flockId,
                            $"Melee,Dmg={data.Agent.AttackDamage}");
                    }
                    data.DamageDealt = true;
                }

                if (data.PhaseTimer <= 0f)
                    return ActionRunState.Completed;
                break;
        }

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
