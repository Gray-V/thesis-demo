using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Ranged attack for GOAPWithBOIDSMovement: Approach → Circle → Fire projectile.
/// Same as PureRangedAttackAction but references GOAPBoidAgent.
/// </summary>
public class GOAPBoidRangedAttackAction : GoapActionBase<GOAPBoidRangedAttackAction.Data>
{
    private const float FiringRange = 12f;
    private const float OrbitRadius = 10f;
    private const float CircleDuration = 1.5f;
    private const float OrbitSpeed = 90f;
    private const float ProjectileSpeed = 15f;
    private const float ProjectileDamage = 30f;
    private const float ProjectileTurnSpeed = 5f;

    public enum Phase { Approach, Circle, Fire }

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public GOAPBoidAgent Agent { get; set; }
        public Phase CurrentPhase { get; set; }
        public float PhaseTimer { get; set; }
        public float CircleAngle { get; set; }
        public bool SlotAcquired { get; set; }
        // Direction from player toward this agent (XZ) — keeps ranged attackers spread during approach
        public Vector3 PreferredApproachDir { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.CurrentPhase = Phase.Approach;
        data.PhaseTimer = 0f;
        data.CircleAngle = 0f;
        data.SlotAcquired = GOAPBoidAgent.RequestAttackSlot(data.Agent.flockId);

        if (data.Target is TransformTarget transformTarget)
            data.Agent.targetPlayer = transformTarget.Transform;

        // Compute natural approach direction from player to this agent (XZ only).
        // Each ranged attacker approaches from its own angle so they arrive at different orbit positions.
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

        // Flock-level participation: register with the manager so it includes this
        // agent when laying out formation slots, and hand it the target so it can
        // advance through Forming -> Locked -> Firing.
        if (data.Agent.flockManager != null)
        {
            data.Agent.flockManager.RegisterRangedParticipant(data.Agent);
            if (data.Agent.targetPlayer != null)
                data.Agent.flockManager.NotifyTargetAcquired(data.Agent.targetPlayer);
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

        // Flock-coordinated volley override.
        // When the manager has entered Forming or Locked, this agent's formation slot
        // is authoritative — steer to it. When the manager enters Firing, short-circuit
        // straight to our own Fire phase so every ranged participant volleys together.
        var mgr = data.Agent.flockManager;
        if (mgr != null && data.Agent.FlockFormationActive)
        {
            var phase = mgr.CurrentRangedPhase;
            if (phase == GOAPBoidFlockManager.RangedAttackPhase.Forming
                || phase == GOAPBoidFlockManager.RangedAttackPhase.Locked)
            {
                data.Agent.SteerToward(data.Agent.FlockFormationTarget);
                return ActionRunState.Continue;
            }
            if (phase == GOAPBoidFlockManager.RangedAttackPhase.Firing)
            {
                data.CurrentPhase = Phase.Fire;
            }
        }

        switch (data.CurrentPhase)
        {
            case Phase.Approach:
                // Approach from the agent's natural direction relative to the player.
                // This ensures ranged attackers arrive at different orbit start angles.
                data.Agent.SteerToward(targetPos + data.PreferredApproachDir * OrbitRadius);
                if (distance <= FiringRange)
                {
                    data.CurrentPhase = Phase.Circle;
                    data.PhaseTimer = CircleDuration;
                    Vector3 offset = agentPos - targetPos;
                    data.CircleAngle = Mathf.Atan2(offset.z, offset.x) * Mathf.Rad2Deg;
                }
                break;

            case Phase.Circle:
                data.CircleAngle += OrbitSpeed * context.DeltaTime;
                float rad = data.CircleAngle * Mathf.Deg2Rad;
                Vector3 orbitPos = targetPos + new Vector3(
                    Mathf.Cos(rad) * OrbitRadius,
                    0f,
                    Mathf.Sin(rad) * OrbitRadius
                );
                orbitPos.y = agentPos.y;
                data.Agent.SteerToward(orbitPos);

                data.PhaseTimer -= context.DeltaTime;
                if (data.PhaseTimer <= 0f)
                    data.CurrentPhase = Phase.Fire;
                break;

            case Phase.Fire:
                if (data.Agent.ProjectilePrefab != null && data.Agent.targetPlayer != null)
                {
                    Vector3 dir = (targetPos - agentPos).normalized;
                    GameObject proj = Object.Instantiate(
                        data.Agent.ProjectilePrefab,
                        agentPos,
                        Quaternion.LookRotation(dir)
                    );
                    BoidProjectile bp = proj.GetComponent<BoidProjectile>();
                    bp?.Initialize(dir, ProjectileSpeed, ProjectileDamage, data.Agent.targetPlayer, ProjectileTurnSpeed);
                    BehavioralMetricsCollector.Instance?.LogEvent(
                        "AttackFired", data.Agent.gameObject.name, data.Agent.flockId,
                        $"Ranged,Dmg={ProjectileDamage}");
                }
                return ActionRunState.Completed;
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
        if (data.Agent != null && data.Agent.flockManager != null)
            data.Agent.flockManager.UnregisterRangedParticipant(data.Agent);
    }

    public override void Stop(IMonoAgent agent, Data data)
    {
        if (data.SlotAcquired)
        {
            GOAPBoidAgent.ReleaseAttackSlot(data.Agent.flockId);
            data.SlotAcquired = false;
        }
        if (data.Agent != null && data.Agent.flockManager != null)
            data.Agent.flockManager.UnregisterRangedParticipant(data.Agent);
    }

    public override void End(IMonoAgent agent, Data data)
    {
        if (data.SlotAcquired)
        {
            GOAPBoidAgent.ReleaseAttackSlot(data.Agent.flockId);
            data.SlotAcquired = false;
        }
        if (data.Agent != null && data.Agent.flockManager != null)
            data.Agent.flockManager.UnregisterRangedParticipant(data.Agent);
        data.Agent.targetPlayer = null;
    }
}
