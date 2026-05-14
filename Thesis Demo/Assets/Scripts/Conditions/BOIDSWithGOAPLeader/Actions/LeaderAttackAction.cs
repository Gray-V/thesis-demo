using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Leader melee attack: Approach → WindUp → Charge.
/// Followers naturally trail the leader toward the player via cohesion redirect.
/// </summary>
public class LeaderAttackAction : GoapActionBase<LeaderAttackAction.Data>
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
        [GetComponent] public BoidAgent Boid { get; set; }
        [GetComponent] public BoidGoapBrain Brain { get; set; }
        public Phase CurrentPhase { get; set; }
        public float PhaseTimer { get; set; }
        public Vector3 ChargeDirection { get; set; }
        public bool DamageDealt { get; set; }
        public Transform PlayerTransform { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        LeaderActionDiagnostic.LogStart(nameof(LeaderAttackAction), data.Boid);
        data.CurrentPhase = Phase.Approach;
        data.PhaseTimer = 0f;
        data.DamageDealt = false;

        if (data.Target is TransformTarget tt)
            data.PlayerTransform = tt.Transform;

        if (data.Brain != null)
        {
            data.Brain.isMovementOverridden = true;
            data.Brain.isGoapAttacking = true;
        }
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (data.Target == null || data.PlayerTransform == null)
            return ActionRunState.Stop;

        Vector3 agentPos = data.Boid.Position;
        Vector3 targetPos = data.Target.Position;
        float distance = Vector3.Distance(agentPos, targetPos);
        float maxSpeed = data.Boid.settings.maxSpeed;
        float maxSteer = data.Boid.settings.maxSteerForce;

        switch (data.CurrentPhase)
        {
            case Phase.Approach:
                Vector3 desired = (targetPos - agentPos).normalized * maxSpeed;
                Vector3 steer = Vector3.ClampMagnitude(desired - data.Boid.velocity, maxSteer);
                data.Boid.velocity += steer * context.DeltaTime;

                if (distance <= TriggerDistance)
                {
                    data.CurrentPhase = Phase.WindUp;
                    data.PhaseTimer = WindUpDuration;
                    data.Boid.velocity *= 0.2f;
                }
                break;

            case Phase.WindUp:
                data.Boid.velocity = Vector3.zero;
                data.PhaseTimer -= context.DeltaTime;
                if (data.PhaseTimer <= 0f)
                {
                    data.ChargeDirection = (targetPos - agentPos).normalized;
                    data.CurrentPhase = Phase.Charge;
                    data.PhaseTimer = ChargeDuration;
                }
                break;

            case Phase.Charge:
                data.Boid.velocity = data.ChargeDirection * maxSpeed * ChargeSpeedMultiplier;
                data.PhaseTimer -= context.DeltaTime;

                if (!data.DamageDealt && distance <= DamageContactDistance)
                {
                    var playerHealth = data.PlayerTransform.GetComponent<PlayerHealth>();
                    if (playerHealth != null)
                    {
                        playerHealth.TakeDamage(data.Boid.settings.attackDamage);
                        BehavioralMetricsCollector.Instance?.LogEvent(
                            "AttackHit", data.Boid.gameObject.name, 0,
                            $"LeaderMelee,Dmg={data.Boid.settings.attackDamage}");
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
        if (data.Brain != null)
            data.Brain.cooldownTimer = data.Boid.settings.attackCooldown;
    }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data)
    {
        if (data.Brain != null)
        {
            data.Brain.isGoapAttacking = false;
            data.Brain.isMovementOverridden = false;
        }
    }
}
