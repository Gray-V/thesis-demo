using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.Profiling;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Central manager for switching between the four experimental agent conditions.
/// Handles agent spawning, scene configuration, and metrics collection per condition.
///
/// Usage: Attach to a GameObject in the scene, configure settings, set CurrentCondition.
/// </summary>
public class ConditionManager : MonoBehaviour
{
    public static ConditionManager Instance { get; private set; }

    /// <summary>
    /// True when the active condition uses GOAP for boid-level combat (BOIDSWithGOAPLeader).
    /// Used by BoidAgent and FlockManager to decide FSM vs GOAP path.
    /// </summary>
    public bool UsesGoapForBoids => CurrentCondition == AgentCondition.BOIDSWithGOAPLeader;

    [Header("Condition Selection")]
    [Tooltip("The currently active experimental condition. Change this to switch AI architectures.")]
    public AgentCondition CurrentCondition = AgentCondition.PureGOAP;

    [Header("Agent Configuration")]
    [Tooltip("Number of agents to spawn (used by PureGOAP and hybrid conditions).")]
    [SerializeField] private int agentCount = 10;

    [Tooltip("Spawn radius around the manager's position.")]
    [SerializeField] private float spawnRadius = 15f;

    [Tooltip("Fraction of agents that are ranged (0 = all melee, 1 = all ranged).")]
    [Range(0f, 1f)]
    [SerializeField] private float rangedAgentRatio = 0.3f;

    [Header("PureGOAP Prefab")]
    [SerializeField] private GameObject pureGOAPAgentPrefab;

    [Header("PureBOIDS Prefabs (FlockManagers)")]
    [Tooltip("FlockManager prefab with MeleeBoidSettings assigned.")]
    [SerializeField] private GameObject meleeFlockManagerPrefab;
    [Tooltip("FlockManager prefab with RangedBoidSettings assigned. Leave empty for melee-only.")]
    [SerializeField] private GameObject rangedFlockManagerPrefab;

    [Header("Hybrid Prefabs (Future)")]
    [SerializeField] private GameObject boidsWithGOAPLeaderPrefab;
    [SerializeField] private GameObject goapWithBOIDSMovementPrefab;

    /// <summary>
    /// Public accessor so PlayMode visual tests can fetch the condition 4 prefab
    /// from the scene's ConditionManager without needing reflection.
    /// </summary>
    public GameObject GOAPWithBOIDSMovementPrefab => goapWithBOIDSMovementPrefab;

    /// <summary>
    /// True when every flock spawned for the current condition is dead.
    /// Checks both FlockManager (cond 2/3) and GOAPBoidFlockManager (cond 4).
    /// Used by ExperimentRunner as a trial-termination condition.
    /// </summary>
    public bool AllFlocksDead
    {
        get
        {
            bool anyFlockFound = false;
            for (int i = 0; i < spawnedAgents.Count; i++)
            {
                var go = spawnedAgents[i];
                if (go == null) continue;

                var pureFlock = go.GetComponent<FlockManager>();
                if (pureFlock != null)
                {
                    anyFlockFound = true;
                    if (!pureFlock.IsDead) return false;
                    continue;
                }

                var goapFlock = go.GetComponent<GOAPBoidFlockManager>();
                if (goapFlock != null)
                {
                    anyFlockFound = true;
                    if (!goapFlock.IsDead) return false;
                }
            }
            // If no flock managers exist (e.g. PureGOAP individual spawn), treat as not-dead
            // so the trial runs its full duration instead of ending instantly.
            return anyFlockFound;
        }
    }

    /// <summary>
    /// Reseeds Unity's Random stream and the serialized randomSeed field so this
    /// trial is reproducible from (baseSeed + run_index). Called by ExperimentRunner
    /// before each trial; reaches every Awake/OnEnable that consumes Random.
    /// </summary>
    public void SetSeed(int seed)
    {
        randomSeed = seed;
        useFixedSeed = true;
        Random.InitState(seed);
    }

    /// <summary>
    /// Overrides the spawn target size for the next SpawnAgentsForCondition call.
    /// ExperimentRunner uses this to sweep flock sizes in a single batch.
    /// </summary>
    public void SetAgentCount(int count)
    {
        agentCount = Mathf.Max(1, count);
    }

    public int AgentCount => agentCount;

    /// <summary>
    /// Toggles stress-mode invulnerability across the player and every spawned flock manager.
    /// In stress mode (true), `PlayerHealth.IsInvulnerableMode` and every flock's `SuspendDamage`
    /// flag are set so no damage lands and live agent count stays at the configured N for the
    /// full trial — giving the FPS benchmark a fixed compute load to compare across conditions.
    /// In combat mode (false), all flags are cleared and the existing damage system runs as
    /// before. ExperimentRunner calls this before each trial. See [[methodology-revisions-2026-04]]
    /// item 1 / 3b for context.
    /// </summary>
    public void SetStressMode(bool stress)
    {
        // Tag fallback when playerTransform Inspector slot is unwired — the 2026-05-04
        // pilot caught this as 18/18 GOAPWithBOIDSMovement stress trials ending in
        // PlayerDeath because the silent-skip on a null Inspector ref left the player
        // vulnerable. Hard error if both paths fail; a contaminated batch is worse than
        // a noisy log.
        var ph = playerTransform != null ? playerTransform.GetComponent<PlayerHealth>() : null;
        if (ph == null)
        {
            var playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO != null) ph = playerGO.GetComponent<PlayerHealth>();
        }
        if (ph != null)
        {
            ph.IsInvulnerableMode = stress;
        }
        else
        {
            Debug.LogError("[ConditionManager] SetStressMode: PlayerHealth not found via " +
                           "playerTransform or 'Player' tag. Stress-mode invulnerability " +
                           "NOT applied — trials will be contaminated.");
        }

        foreach (var fm in FindObjectsByType<FlockManager>(FindObjectsSortMode.None))
            fm.SuspendDamage = stress;
        foreach (var gfm in FindObjectsByType<GOAPBoidFlockManager>(FindObjectsSortMode.None))
            gfm.SuspendDamage = stress;
    }

    [Header("Reproducibility")]
    [Tooltip("Use a fixed seed so spawns and Random-based behavior are identical across runs.")]
    [SerializeField] private bool useFixedSeed = true;
    [SerializeField] private int randomSeed = 42;

    [Header("Comparison Parity")]
    [Tooltip("HP pool applied to each flock manager in conditions 2, 3, 4 so the only " +
             "variable across conditions is decision architecture. Must match GOAPBoidFlockManager.maxFlockHealth.")]
    [SerializeField] private float comparisonHPPerFlock = 3000f;

    [Header("Runtime State")]
    [SerializeField] private List<GameObject> spawnedAgents = new List<GameObject>();

    [Header("GOAP Reference")]
    [Tooltip("The GoapBehaviour in the scene (GOAP Manager).")]
    [SerializeField] private GoapBehaviour goapBehaviour;

    [Header("Player Reference")]
    [Tooltip("Reference to the player transform (target for all agents).")]
    public Transform playerTransform;

    private void Awake()
    {
        Instance = this;

        // Seed at Awake — earliest hook — so any pre-existing scene GameObject's
        // Awake/OnEnable (bob/weave phase offsets, random initial velocities, etc.)
        // consumes from the seeded stream. SpawnAgentsForCondition re-seeds before
        // spawning so both passes start from the same state.
        if (useFixedSeed)
            Random.InitState(randomSeed);
    }

    private void Start()
    {
        SpawnAgentsForCondition();
    }

    /// <summary>
    /// Spawns agents for the currently selected condition.
    /// Clears any existing agents first.
    /// </summary>
    public void SpawnAgentsForCondition()
    {
        Profiler.BeginSample("ConditionManager.SpawnAgentsForCondition");

        if (useFixedSeed)
            Random.InitState(randomSeed);

        ClearAgents();

        switch (CurrentCondition)
        {
            case AgentCondition.PureBOIDS:
                SpawnPureBOIDSFlocks();
                break;

            case AgentCondition.BOIDSWithGOAPLeader:
                StartCoroutine(SpawnBOIDSWithGOAPLeaderFlocks());
                break;

            case AgentCondition.GOAPWithBOIDSMovement:
                SpawnGOAPWithBOIDSMovementFlocks();
                break;

            default:
                SpawnIndividualAgents();
                break;
        }

        Debug.Log($"[ConditionManager] Spawned agents for condition: {CurrentCondition}");
        Profiler.EndSample();
    }

    /// <summary>
    /// Spawns FlockManager(s) for PureBOIDS condition.
    /// Each FlockManager spawns its own boids internally via Start().
    /// </summary>
    private void SpawnPureBOIDSFlocks()
    {
        // Apply thesis parity: same headcount + HP pool as conditions 3 & 4.
        int rangedCount = Mathf.RoundToInt(agentCount * rangedAgentRatio);
        int meleeCount = agentCount - rangedCount;

        if (meleeFlockManagerPrefab != null)
        {
            GameObject melee = Instantiate(meleeFlockManagerPrefab, transform.position, Quaternion.identity, transform);
            melee.name = "PureBOIDS_MeleeFlockManager";
            // Overrides must be set BEFORE Start() fires; Instantiate runs Awake
            // synchronously but defers Start to next frame — safe window.
            melee.GetComponent<FlockManager>()?.ApplyComparisonOverrides(meleeCount, comparisonHPPerFlock);
            spawnedAgents.Add(melee);
        }

        if (rangedFlockManagerPrefab != null)
        {
            // Offset ranged flock slightly so they don't overlap at spawn
            Vector3 rangedPos = transform.position + Vector3.right * spawnRadius * 0.5f;
            GameObject ranged = Instantiate(rangedFlockManagerPrefab, rangedPos, Quaternion.identity, transform);
            ranged.name = "PureBOIDS_RangedFlockManager";
            ranged.GetComponent<FlockManager>()?.ApplyComparisonOverrides(rangedCount, comparisonHPPerFlock);
            spawnedAgents.Add(ranged);
        }

        if (meleeFlockManagerPrefab == null && rangedFlockManagerPrefab == null)
        {
            Debug.LogError("[ConditionManager] No FlockManager prefabs assigned for PureBOIDS condition!");
        }
    }

    /// <summary>
    /// Spawns FlockManager(s) for BOIDSWithGOAPLeader condition.
    /// Uses a coroutine to wait one frame so FlockManager.Start() finishes spawning boids,
    /// then designates boid[0] as the GOAP leader.
    /// </summary>
    private IEnumerator SpawnBOIDSWithGOAPLeaderFlocks()
    {
        // Parity with conditions 2 & 4 — same headcount + HP pool.
        int rangedCount = Mathf.RoundToInt(agentCount * rangedAgentRatio);
        int meleeCount = agentCount - rangedCount;

        // Spawn FlockManagers (same prefabs as PureBOIDS)
        List<FlockManager> managers = new List<FlockManager>();

        if (meleeFlockManagerPrefab != null)
        {
            GameObject melee = Instantiate(meleeFlockManagerPrefab, transform.position, Quaternion.identity, transform);
            melee.name = "LeaderBOIDS_MeleeFlockManager";
            melee.GetComponent<FlockManager>()?.ApplyComparisonOverrides(meleeCount, comparisonHPPerFlock);
            spawnedAgents.Add(melee);
            managers.Add(melee.GetComponent<FlockManager>());
        }

        if (rangedFlockManagerPrefab != null)
        {
            Vector3 rangedPos = transform.position + Vector3.right * spawnRadius * 0.5f;
            GameObject ranged = Instantiate(rangedFlockManagerPrefab, rangedPos, Quaternion.identity, transform);
            ranged.name = "LeaderBOIDS_RangedFlockManager";
            ranged.GetComponent<FlockManager>()?.ApplyComparisonOverrides(rangedCount, comparisonHPPerFlock);
            spawnedAgents.Add(ranged);
            managers.Add(ranged.GetComponent<FlockManager>());
        }

        // Wait one frame for FlockManager.Start() → SpawnFlock() to complete
        yield return null;

        // Designate boid[0] in each flock as leader
        foreach (var mgr in managers)
        {
            if (mgr == null || mgr.Boids.Count == 0) continue;

            BoidAgent leader = mgr.Boids[0];
            mgr.SetLeader(leader);

            // Attach leader GOAP brain (disable the default BoidGoapBrain to avoid conflicts)
            var defaultBrain = leader.GetComponent<BoidGoapBrain>();
            if (defaultBrain != null)
                defaultBrain.enabled = false;

            leader.gameObject.AddComponent<LeaderGoapBrain>();

            // Wire GOAP agent type
            var provider = leader.GetComponent<GoapActionProvider>();
            if (provider != null && goapBehaviour != null)
            {
                provider.AgentType = goapBehaviour.GetAgentType("LeaderBoid");
            }

            // Visual distinction: gold color, slightly larger
            Renderer rend = leader.GetComponentInChildren<Renderer>();
            if (rend != null)
                rend.material.color = new Color(1f, 0.84f, 0f); // Gold

            leader.transform.localScale *= 1.3f;

            Debug.Log($"[ConditionManager] Designated leader in {mgr.name}: {leader.name}");
        }
    }

    /// <summary>
    /// Spawns individual agents for conditions that use per-agent prefabs (PureGOAP, hybrids).
    /// </summary>
    private void SpawnIndividualAgents()
    {
        GameObject prefab = GetPrefabForCondition(CurrentCondition);
        if (prefab == null)
        {
            Debug.LogError($"[ConditionManager] No prefab assigned for condition: {CurrentCondition}");
            return;
        }

        for (int i = 0; i < agentCount; i++)
        {
            Vector3 spawnPos = transform.position + GetSpawnOffset(i);
            Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject agent = Instantiate(prefab, spawnPos, spawnRot, transform);
            agent.name = $"{CurrentCondition}_Agent_{i}";

            InitializeAgentForCondition(agent, CurrentCondition);

            spawnedAgents.Add(agent);
        }
    }

    /// <summary>
    /// Spawns GOAPWithBOIDSMovement agents as separate melee and ranged flocks.
    /// Each flock has its own flockId so BOIDS forces only apply within the flock.
    /// </summary>
    private void SpawnGOAPWithBOIDSMovementFlocks()
    {
        GameObject prefab = goapWithBOIDSMovementPrefab;
        if (prefab == null)
        {
            Debug.LogError("[ConditionManager] No prefab assigned for GOAPWithBOIDSMovement!");
            return;
        }

        int rangedCount = Mathf.RoundToInt(agentCount * rangedAgentRatio);
        int meleeCount = agentCount - rangedCount;

        // Spawn one flock manager per flock — these own pooled HP, centroid,
        // leash, and coordinated attack phases for GOAPWithBOIDSMovement.
        var meleeManager = CreateFlockManager("GOAPBoidFlock_Melee", FlockType.Melee, 0, meleeCount, transform.position);
        Vector3 rangedOffset = Vector3.right * spawnRadius * 0.5f;
        var rangedManager = CreateFlockManager("GOAPBoidFlock_Ranged", FlockType.Ranged, 1, rangedCount, transform.position + rangedOffset);

        // Spawn melee flock (flockId = 0)
        for (int i = 0; i < meleeCount; i++)
        {
            Vector3 spawnPos = transform.position + GetSpawnOffset(i);
            Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            GameObject agent = Instantiate(prefab, spawnPos, spawnRot, transform);
            agent.name = $"GOAPBoid_Melee_{i}";

            var gbAgent = agent.GetComponent<GOAPBoidAgent>();
            if (gbAgent != null)
            {
                gbAgent.SetAttackType(AttackType.Melee);
                gbAgent.flockId = 0;
                meleeManager?.RegisterAgent(gbAgent);
            }

            var gbProvider = agent.GetComponent<GoapActionProvider>();
            if (gbProvider != null && goapBehaviour != null)
                gbProvider.AgentType = goapBehaviour.GetAgentType("GOAPBoidAgent");

            spawnedAgents.Add(agent);
        }

        // Spawn ranged flock (flockId = 1), offset from melee
        for (int i = 0; i < rangedCount; i++)
        {
            Vector3 spawnPos = transform.position + rangedOffset + GetSpawnOffset(i);
            Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            GameObject agent = Instantiate(prefab, spawnPos, spawnRot, transform);
            agent.name = $"GOAPBoid_Ranged_{i}";

            var gbAgent = agent.GetComponent<GOAPBoidAgent>();
            if (gbAgent != null)
            {
                gbAgent.SetAttackType(AttackType.Ranged);
                gbAgent.flockId = 1;
                rangedManager?.RegisterAgent(gbAgent);
            }

            var gbProvider = agent.GetComponent<GoapActionProvider>();
            if (gbProvider != null && goapBehaviour != null)
                gbProvider.AgentType = goapBehaviour.GetAgentType("GOAPBoidAgent");

            spawnedAgents.Add(agent);
        }

        Debug.Log($"[ConditionManager] Spawned GOAPWithBOIDSMovement: {meleeCount} melee (flock 0), {rangedCount} ranged (flock 1)");
    }

    /// <summary>
    /// Create and configure a GOAPBoidFlockManager GameObject for condition 4.
    /// Manager GO is parented to this ConditionManager so ClearAgents tears it
    /// down along with the agents it owns.
    /// </summary>
    private GOAPBoidFlockManager CreateFlockManager(string name, FlockType type, int id, int size, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.position = position;
        var mgr = go.AddComponent<GOAPBoidFlockManager>();
        mgr.Configure(type, id, size);
        spawnedAgents.Add(go); // reuse existing cleanup path
        return mgr;
    }

    /// <summary>
    /// Clears all currently spawned agents and flocks.
    /// </summary>
    public void ClearAgents()
    {
        foreach (GameObject agent in spawnedAgents)
        {
            if (agent != null)
                Destroy(agent);
        }
        spawnedAgents.Clear();
        GOAPBoidAgent.ResetAttackSlots();
    }

    private GameObject GetPrefabForCondition(AgentCondition condition)
    {
        return condition switch
        {
            AgentCondition.PureGOAP => pureGOAPAgentPrefab,
            AgentCondition.BOIDSWithGOAPLeader => boidsWithGOAPLeaderPrefab,
            AgentCondition.GOAPWithBOIDSMovement => goapWithBOIDSMovementPrefab,
            _ => null
        };
    }

    private void InitializeAgentForCondition(GameObject agent, AgentCondition condition)
    {
        switch (condition)
        {
            case AgentCondition.PureGOAP:
                var provider = agent.GetComponent<GoapActionProvider>();
                if (provider != null && goapBehaviour != null)
                {
                    provider.AgentType = goapBehaviour.GetAgentType("PureGOAPAgent");
                }
                // Assign melee/ranged based on ratio
                var pureAgent = agent.GetComponent<PureGOAPAgent>();
                if (pureAgent != null)
                {
                    int rangedCount = Mathf.RoundToInt(agentCount * rangedAgentRatio);
                    bool isRanged = spawnedAgents.Count < rangedCount;
                    pureAgent.SetAttackType(isRanged ? AttackType.Ranged : AttackType.Melee);
                }
                break;

            case AgentCondition.BOIDSWithGOAPLeader:
                // Handled by SpawnBOIDSWithGOAPLeaderFlocks() coroutine — not individual agents
                break;

            case AgentCondition.GOAPWithBOIDSMovement:
                var gbProvider = agent.GetComponent<GoapActionProvider>();
                if (gbProvider != null && goapBehaviour != null)
                {
                    gbProvider.AgentType = goapBehaviour.GetAgentType("GOAPBoidAgent");
                }
                var gbAgent = agent.GetComponent<GOAPBoidAgent>();
                if (gbAgent != null)
                {
                    int rangedCount = Mathf.RoundToInt(agentCount * rangedAgentRatio);
                    bool isRanged = spawnedAgents.Count < rangedCount;
                    gbAgent.SetAttackType(isRanged ? AttackType.Ranged : AttackType.Melee);
                }
                break;
        }
    }

    private Vector3 GetSpawnOffset(int index)
    {
        float angle = (360f / agentCount) * index * Mathf.Deg2Rad;
        float randomRadius = Random.Range(spawnRadius * 0.5f, spawnRadius);

        return new Vector3(
            Mathf.Cos(angle) * randomRadius,
            0f,
            Mathf.Sin(angle) * randomRadius
        );
    }

    private void OnValidate()
    {
        // Floor at 1; no upper clamp. The earlier [1, 100] clamp predated the
        // batch sweep up to N=800 and silently truncated Inspector edits — a
        // direct edit to "800" saved as 100 with no warning. The runner's
        // SetAgentCount() does its own validation, and batch sizes come from
        // ExperimentRunner.agentCountsToTest (not this field).
        if (agentCount < 1) agentCount = 1;
    }

    #if UNITY_EDITOR
    [ContextMenu("Spawn Agents for Current Condition")]
    private void EditorSpawnAgents()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ConditionManager] Can only spawn agents during Play Mode.");
            return;
        }
        SpawnAgentsForCondition();
    }

    [ContextMenu("Clear All Agents")]
    private void EditorClearAgents()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ConditionManager] Can only clear agents during Play Mode.");
            return;
        }
        ClearAgents();
    }
    #endif
}
