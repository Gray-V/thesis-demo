using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Goal selection brain for PureGOAP agents.
/// Requests GOAP goals based on player proximity and agent state.
///
/// Logic (priority order):
/// - If health low AND player nearby → Request FleeGoal
/// - If player nearby AND not on cooldown → Request AttackGoal
/// - Otherwise → Request WanderGoal (swarm movement)
/// </summary>
[RequireComponent(typeof(PureGOAPAgent))]
[RequireComponent(typeof(GoapActionProvider))]
public class PureGOAPBrain : MonoBehaviour
{
    [SerializeField] private float playerDetectionRange = 30f;
    [SerializeField] private string playerTag = "Player";

    private PureGOAPAgent agent;
    private GoapActionProvider provider;
    private Transform playerTransform;

    private void Awake()
    {
        agent = GetComponent<PureGOAPAgent>();
        provider = GetComponent<GoapActionProvider>();
    }

    private void Update()
    {
        if (provider == null || provider.AgentType == null)
            return;

        // Cache player reference
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
                playerTransform = player.transform;
        }

        // Check if player is within detection range
        bool playerNearby = false;
        if (playerTransform != null)
        {
            float dist = Vector3.Distance(transform.position, playerTransform.position);
            playerNearby = dist <= playerDetectionRange;
        }

        // Goal selection: flee > attack > wander
        if (playerNearby && agent.HealthPercent < 0.3f)
        {
            provider.RequestGoal<PureFleeGoal>();
        }
        else if (playerNearby && agent.cooldownTimer <= 0f && PureGOAPAgent.SwarmAttackCooldown <= 0f)
        {
            if (agent.AgentAttackType == AttackType.Ranged)
                provider.RequestGoal<PureRangedAttackGoal>();
            else
                provider.RequestGoal<PureAttackGoal>();
        }
        else
        {
            provider.RequestGoal<PureWanderGoal>();
        }
    }
}
