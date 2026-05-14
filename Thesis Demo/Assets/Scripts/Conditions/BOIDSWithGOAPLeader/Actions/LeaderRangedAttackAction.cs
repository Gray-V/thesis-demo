using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Leader ranged attack: <c>Approach</c> until in firing range, then <c>Fire</c>
/// one projectile and complete. Used by ranged-flock leaders when the goal
/// resolver returns <c>GoalType.RangedAttack</c> (player at long range, leader
/// should close to firing distance and engage from range).
///
/// Mirrors <see cref="LeaderKiteAction"/>'s projectile-firing code path with
/// an Approach phase instead of Retreat. The two actions are complementary:
/// Kite handles "player too close → backpedal and fire", RangedAttack handles
/// "player too far → close and fire".
///
/// Created 2026-05-05 to fix the ranged-leader bug discovered via the four-
/// bucket diagnostic batch: <see cref="LeaderGoapBrain"/>'s goal-request
/// switch had no case for <c>GoalType.RangedAttack</c>, so the resolver's
/// flips were silently downgraded to <c>LeaderWanderGoal</c>. The ranged
/// leader was Wandering through every v1 trial while the metric logged it
/// as engaged.
/// </summary>
public class LeaderRangedAttackAction : GoapActionBase<LeaderRangedAttackAction.Data>
{
    private const float FiringRange = 12f;
    private const float ApproachTimeout = 8f;
    private const float ProjectileSpeed = 15f;
    private const float ProjectileDamage = 30f;
    private const float ProjectileTurnSpeed = 5f;

    public enum Phase { Approach, Fire }

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public BoidAgent Boid { get; set; }
        [GetComponent] public BoidGoapBrain Brain { get; set; }
        public Phase CurrentPhase { get; set; }
        public float ApproachTimer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        LeaderActionDiagnostic.LogStart(nameof(LeaderRangedAttackAction), data.Boid);
        data.CurrentPhase = Phase.Approach;
        data.ApproachTimer = ApproachTimeout;

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

        switch (data.CurrentPhase)
        {
            case Phase.Approach:
                // Drive straight at the player until inside firing range. Velocity is
                // set unconditionally each frame — the leash in LeaderGoapBrain.Update
                // applies its slow-factor on top, keeping the leader bounded to the
                // flock if it's outpacing followers.
                Vector3 approachDir = (targetPos - agentPos).normalized;
                data.Boid.velocity = approachDir * data.Boid.settings.maxSpeed;

                if (distance <= FiringRange)
                {
                    data.CurrentPhase = Phase.Fire;
                }
                else
                {
                    // Bound the chase so a fleeing-faster-than-leader player can't
                    // pin the leader in Approach forever. On timeout we Complete so
                    // the resolver gets to re-evaluate (likely back to RangedAttack
                    // if player is still detected, or Wander otherwise).
                    data.ApproachTimer -= context.DeltaTime;
                    if (data.ApproachTimer <= 0f)
                        return ActionRunState.Completed;
                }
                break;

            case Phase.Fire:
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
                        "AttackFired", data.Boid.gameObject.name,
                        LeaderGoapBrain.ResolveFlockId(data.Boid),
                        $"LeaderRangedAttack,Dmg={ProjectileDamage}");
                }
                return ActionRunState.Completed;
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
