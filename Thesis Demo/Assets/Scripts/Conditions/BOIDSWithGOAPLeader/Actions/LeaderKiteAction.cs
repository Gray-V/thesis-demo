using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Leader kite: backpedal away from player while firing a projectile.
/// Two phases: Retreat (when player too close) → Fire (when in kite range).
/// Followers trail the retreating leader via cohesion redirect.
/// </summary>
public class LeaderKiteAction : GoapActionBase<LeaderKiteAction.Data>
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
        [GetComponent] public BoidAgent Boid { get; set; }
        [GetComponent] public BoidGoapBrain Brain { get; set; }
        public Phase CurrentPhase { get; set; }
        public float KiteTimer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        LeaderActionDiagnostic.LogStart(nameof(LeaderKiteAction), data.Boid);
        data.CurrentPhase = Phase.Retreat;
        data.KiteTimer = 8f;

        if (data.Brain != null)
        {
            data.Brain.isMovementOverridden = true;
            data.Brain.isGoapAttacking = true;
        }
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (data.Target == null)
            return ActionRunState.Stop;

        Vector3 agentPos = data.Boid.Position;
        Vector3 targetPos = data.Target.Position;
        float distance = Vector3.Distance(agentPos, targetPos);

        float minDist = data.Boid.settings != null ? data.Boid.settings.kiteMinDistance : KiteMinDistance;
        float maxDist = data.Boid.settings != null ? data.Boid.settings.kiteMaxDistance : KiteMaxDistance;

        switch (data.CurrentPhase)
        {
            case Phase.Retreat:
                // Backpedal away from player
                Vector3 retreatDir = (agentPos - targetPos).normalized;
                data.Boid.velocity = retreatDir * data.Boid.settings.maxSpeed;

                // If we're in firing range, transition to Fire
                if (distance >= minDist && distance <= maxDist)
                    data.CurrentPhase = Phase.Fire;
                break;

            case Phase.Fire:
                // Fire projectile at player
                if (data.Boid.settings.projectilePrefab != null)
                {
                    Vector3 fireDir = (targetPos - agentPos).normalized;
                    GameObject proj = Object.Instantiate(
                        data.Boid.settings.projectilePrefab,
                        agentPos,
                        Quaternion.LookRotation(fireDir)
                    );
                    Transform playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
                    BoidProjectile bp = proj.GetComponent<BoidProjectile>();
                    bp?.Initialize(fireDir, ProjectileSpeed, ProjectileDamage, playerTransform, ProjectileTurnSpeed);
                    BehavioralMetricsCollector.Instance?.LogEvent(
                        "AttackFired", data.Boid.gameObject.name, 0,
                        $"LeaderKite,Dmg={ProjectileDamage}");
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
        if (data.Brain != null)
            data.Brain.cooldownTimer = data.Boid.settings.attackCooldown;
    }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data)
    {
        if (data.Brain != null)
        {
            data.Brain.isMovementOverridden = false;
            data.Brain.isGoapAttacking = false;
        }
    }
}
