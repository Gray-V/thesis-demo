using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Per-boid GOAP controller. Attached alongside BoidAgent, AgentBehaviour, and GoapActionProvider.
/// Holds all transient attack state that GOAP actions read/write, and drives goal selection each frame.
///
/// Components are inert when the active condition does not use GOAP for boids.
/// </summary>
public class BoidGoapBrain : MonoBehaviour
{
    // ── Transient attack state ─────────────────────────────────────
    // These replace the private per-boid fields that the legacy FSM managed inside BoidAgent.

    /// <summary>Remaining seconds until this boid can start a new attack cycle.</summary>
    public float cooldownTimer;

    /// <summary>True once WindUpAction.Complete() has run and a charge direction is locked.</summary>
    public bool windUpComplete;

    /// <summary>True once CircleAction.Complete() has run and the boid is ready to fire.</summary>
    public bool circleDone;

    /// <summary>Normalised direction locked at the end of wind-up, used by ChargeAction.</summary>
    public Vector3 chargeDirection;

    /// <summary>Current angle (radians) along the orbit circle, advanced by CircleAction.</summary>
    public float circleAngle;

    /// <summary>Countdown for the wind-up phase, set by WindUpAction.Start().</summary>
    public float windUpTimer;

    /// <summary>Countdown for the orbit phase, set by CircleAction.Start().</summary>
    public float circleTimer;

    /// <summary>Countdown for the charge phase, set by ChargeAction.Start().</summary>
    public float chargeTimer;

    /// <summary>Whether damage has been dealt during the current charge (prevents multi-hit).</summary>
    public bool damageDealtThisCharge;

    /// <summary>True while an attack slot has been successfully acquired from FlockManager.</summary>
    public bool gotAttackSlot;

    // ── GOAP status flags — read by BoidAgent ──────────────────────

    /// <summary>
    /// True while this boid is executing an attack action (WindUp, Charge, Circle, or Fire).
    /// Read by BoidAgent.IsAttacking when the GOAP toggle is on.
    /// </summary>
    public bool isGoapAttacking;

    /// <summary>
    /// True while a GOAP action owns the boid's velocity (suppresses flocking forces).
    /// Read by BoidAgent.IsMovementOverridden when the GOAP toggle is on.
    /// </summary>
    public bool isMovementOverridden;

    // ── Internal references ────────────────────────────────────────

    private BoidAgent boid;
    private GoapActionProvider provider;

    private void Awake()
    {
        boid = GetComponent<BoidAgent>();
        provider = GetComponent<GoapActionProvider>();

        // Link back so BoidAgent can read our flags without requiring a GetComponent each frame.
        if (boid != null)
            boid.goapBrain = this;
    }

    private void Update()
    {
        bool useGoap = ConditionManager.Instance != null && ConditionManager.Instance.UsesGoapForBoids;
        if (!useGoap || provider == null || provider.AgentType == null)
            return;

        var manager = boid?.manager;
        if (manager == null)
            return;

        // ── Tick cooldown ──────────────────────────────────────────
        // The brain owns the cooldown so sensors can read it independently of the FSM.
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        // ── Goal selection ─────────────────────────────────────────

        // Leader-mimic path (BOIDSWithGOAPLeader): when this boid is a non-leader
        // follower and the leader is performing Attack/Kite, mirror the leader's
        // combat style. Without this, followers stay in FlockGoal because the
        // FSM is suppressed by useGoap and manager.State never reaches Engaging,
        // so the leader fights solo while the flock just coheres.
        var leader = manager.LeaderBoid;
        bool isFollower = leader != null && boid != leader;
        if (isFollower && cooldownTimer <= 0f && manager.CanAttack && !manager.IsDead)
        {
            var leaderBrain = leader.GetComponent<LeaderGoapBrain>();
            if (leaderBrain != null)
            {
                var lg = leaderBrain.currentGoalType;
                bool leaderAttacking = lg == GoalPriorityResolver.GoalType.Attack
                                    || lg == GoalPriorityResolver.GoalType.Kite;
                if (leaderAttacking)
                {
                    if (leader.settings.flockType == FlockType.Melee)
                        provider.RequestGoal<MeleeAttackGoal>();
                    else
                        provider.RequestGoal<RangedAttackGoal>();
                    return;
                }
            }
        }

        if (manager.State != FlockManager.FlockState.Engaging || manager.IsDead)
        {
            provider.RequestGoal<FlockGoal>();
            return;
        }

        if (cooldownTimer > 0f)
        {
            provider.RequestGoal<FlockGoal>();
            return;
        }

        float dist = manager.Target != null
            ? Vector3.Distance(boid.Position, manager.Target.position)
            : float.MaxValue;

        if (dist <= boid.settings.attackTriggerDistance && manager.CanAttack)
        {
            if (boid.settings.flockType == FlockType.Melee)
                provider.RequestGoal<MeleeAttackGoal>();
            else
                provider.RequestGoal<RangedAttackGoal>();
        }
        else
        {
            provider.RequestGoal<FlockGoal>();
        }
    }
}
