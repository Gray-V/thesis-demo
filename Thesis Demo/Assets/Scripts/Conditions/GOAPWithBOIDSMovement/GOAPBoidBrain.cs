using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Goal selection brain for GOAPWithBOIDSMovement agents.
/// Same priority logic as PureGOAP: Flee > Attack > Wander.
/// </summary>
[RequireComponent(typeof(GOAPBoidAgent))]
[RequireComponent(typeof(GoapActionProvider))]
public class GOAPBoidBrain : MonoBehaviour
{
    [SerializeField] private float playerDetectionRange = 30f;
    [SerializeField] private string playerTag = "Player";

    private GOAPBoidAgent agent;
    private GoapActionProvider provider;
    private Transform playerTransform;

    /// <summary>The last resolved goal type (for metrics/testing).</summary>
    [HideInInspector] public GoalPriorityResolver.GoalType currentGoalType;
    private GoalPriorityResolver.GoalType previousGoalType = GoalPriorityResolver.GoalType.Wander;

    private void Awake()
    {
        agent = GetComponent<GOAPBoidAgent>();
        provider = GetComponent<GoapActionProvider>();
    }

    private void Update()
    {
        if (provider == null || provider.AgentType == null)
            return;

        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
                playerTransform = player.transform;
        }

        bool playerNearby = false;
        if (playerTransform != null)
        {
            float dist = Vector3.Distance(transform.position, playerTransform.position);
            playerNearby = dist <= playerDetectionRange;
        }

        // Goal priority: Scatter > Flee > Attack/Kite > Regroup > Flank > Guard > Wander
        float healthPercent = agent.HealthPercent;
        bool cooldownReady = agent.cooldownTimer <= 0f && GOAPBoidAgent.IsFlockCooldownReady(agent.flockId);
        bool isRanged = agent.AgentAttackType == AttackType.Ranged;
        float playerDist = playerTransform != null
            ? Vector3.Distance(transform.position, playerTransform.position)
            : float.MaxValue;

        // Check isolation from flock centroid (same flockId only)
        bool isIsolated = false;
        int flockCount = GOAPBoidAgent.GetFlockCount(agent.flockId);
        if (flockCount > 1)
        {
            Vector3 centroid = GOAPBoidAgent.GetFlockCentroid(agent.flockId);
            isIsolated = Vector3.Distance(transform.position, centroid) > 20f;
        }

        bool slotAvailable = GOAPBoidAgent.CanAttack(agent.flockId);

        // Hysteresis buffer matches LeaderGoapBrain so v2 batch sweeps the same anti-
        // oscillation behavior across both GOAP-based conditions. Detail in
        // wiki/methodology-revisions-2026-04 item 12.
        const float hysteresisBuffer = 0.05f;
        var goal = GoalPriorityResolver.ResolveGOAPBoidGoal(
            healthPercent, playerNearby, cooldownReady, isRanged,
            playerDist, isIsolated, flockCount, slotAvailable,
            previousGoal: previousGoalType, hysteresisBuffer: hysteresisBuffer);

        currentGoalType = goal;

        if (goal != previousGoalType)
        {
            BehavioralMetricsCollector.Instance?.LogEvent(
                "GoalChange", gameObject.name, agent.flockId, $"{previousGoalType}→{goal}");
            previousGoalType = goal;
        }

        switch (goal)
        {
            case GoalPriorityResolver.GoalType.Scatter:
                provider.RequestGoal<ScatterGoal>(); break;
            case GoalPriorityResolver.GoalType.Flee:
                provider.RequestGoal<PureFleeGoal>(); break;
            case GoalPriorityResolver.GoalType.Attack:
                provider.RequestGoal<PureAttackGoal>(); break;
            case GoalPriorityResolver.GoalType.RangedAttack:
                provider.RequestGoal<PureRangedAttackGoal>(); break;
            case GoalPriorityResolver.GoalType.Kite:
                provider.RequestGoal<KiteGoal>(); break;
            case GoalPriorityResolver.GoalType.Regroup:
                provider.RequestGoal<RegroupGoal>(); break;
            case GoalPriorityResolver.GoalType.Flank:
                provider.RequestGoal<FlankGoal>(); break;
            case GoalPriorityResolver.GoalType.Guard:
                provider.RequestGoal<GuardGoal>(); break;
            default:
                provider.RequestGoal<PureWanderGoal>(); break;
        }
    }
}
