using System.Collections.Generic;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

public class FlockManager : MonoBehaviour
{
    // Extended state machine for thesis parity: conditions 3 & 4 cover Flee/Scatter/
    // Kite/Flank/Guard/Regroup via GOAP; PureBOIDS now covers them at the flock level
    // via FlockStateResolver. See FlockStateResolver.Resolve for transition rules.
    public enum FlockState
    {
        Idle,
        Grouping,
        Engaging,
        Fleeing,
        Scattering,
        Kiting,
        Flanking,
        Guarding,
        Regrouping
    }
    public enum RangedAttackPhase { None, Forming, Locked, Firing, Recovering }
    public enum MeleeAttackPhase { None, WindUp, Charging, Recovering }

    [SerializeField] private BoidSettings settings;
    [SerializeField] private GameObject boidPrefab;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private List<BoidAgent> boids = new List<BoidAgent>();
    private List<BoidAgent> foreignBoids = new List<BoidAgent>();
    private Transform target;
    private SphereCollider aggroTrigger;
    private FlockState state = FlockState.Idle;
    private int originalFlockSize;
    private float currentFlockHealth;
    private int currentAttackerCount;

    // Scatter auto-expires after settings.scatterDuration; Regroup clears once the
    // flock tightens back within regroupRadius (see UpdateFlockState).
    private float scatterTimer;

    // Minimum dwell time in a state before another transition is allowed.
    // Without this the resolver oscillates at frame rate when a continuous input
    // (HP%, playerDist) sits right at a threshold — hundreds of Fleeing↔Guarding
    // flips per second were observed in the 2026-04-23 pilot before this guard
    // was added. Scattering has its own timer and is exempt.
    private const float MinStateDwellSeconds = 0.25f;
    private float stateEnterTime = -999f;

    // Tuning-parity overrides set by ConditionManager before Start() fires so
    // conditions 2/3/4 spawn identical flock sizes + HP pools.
    private int flockSizeOverride = -1;
    private float maxHealthOverride = -1f;

    // Flock ranged attack state
    private RangedAttackPhase rangedPhase = RangedAttackPhase.None;
    private float rangedPhaseTimer;
    private float flockAttackCooldownTimer;
    private Vector3[] formationSlots;
    private float formationOrbitAngle;

    // Flock melee attack state
    private MeleeAttackPhase meleePhase = MeleeAttackPhase.None;
    private float meleePhaseTimer;
    private float flockMeleeCooldownTimer;
    private bool meleeWaveDamageDealt;

    public FlockType FlockType => settings.flockType;
    public BoidSettings Settings => settings;
    public IReadOnlyList<BoidAgent> Boids => boids;
    public int BoidCount => boids.Count;
    public Transform Target => target;
    public FlockState State => state;

    // Leader support for BOIDSWithGOAPLeader condition
    private BoidAgent leaderBoid;
    public BoidAgent LeaderBoid => leaderBoid;
    public void SetLeader(BoidAgent leader) { leaderBoid = leader; }

    /// <summary>
    /// Stamped by <see cref="LeaderGoapBrain"/> at the moment the leader's resolved
    /// goal changes; consumed once by the next <c>LateUpdate</c> cohesion redirect
    /// to emit a single <c>FollowerReact</c> diagnostic event. Sentinel = -1f.
    /// Only meaningful while <see cref="LeaderGoapBrain.DiagnosticLogging"/> is on.
    /// Public so the static toggle's hand-off doesn't require routing through a
    /// dedicated method (and so EditMode tests can poke it directly).
    /// </summary>
    public float pendingFollowerReactTime = -1f;

    // Number of scatter subgroups. Each boid is assigned a subgroupId in [0, count)
    // at spawn time (see SpawnFlock / AddBoids); ScatterDirectionPicker uses these
    // buckets so the flock disperses in a few coherent directions instead of every
    // boid picking independently.
    public const int ScatterSubgroupCount = 3;

    // Diagnostic toggle from the 2026-05-04 20-agent-floor investigation. The
    // investigation concluded the original artifact was an override-not-applied
    // bug fixed by the 2026-04-25 round. The instrumentation is left in place,
    // off by default, for future debugging. Toggle via the "Thesis -> Toggle
    // Floor Diagnostic" Editor menu.
    public static bool DiagnosticLogging = false;
    private int lastLoggedTargetCount = int.MinValue;

    // Movement diagnostics (defense-day debugging). When true, LateUpdate logs a
    // per-flock summary plus a detailed trace of boids[0] roughly every 0.5s, all
    // tagged [BoidDebug]. Flip this default to false to silence before a showcase.
    public static bool MovementDebug = true;
    private float movementDebugTimer;

    public bool IsDead => currentFlockHealth <= 0f;
    /// <summary>Max HP, honoring the ConditionManager override when set.</summary>
    public float EffectiveMaxHealth => maxHealthOverride > 0f
        ? maxHealthOverride
        : (settings != null ? settings.maxHealth : 0f);
    public float HealthPercent => EffectiveMaxHealth > 0f ? currentFlockHealth / EffectiveMaxHealth : 0f;

    /// <summary>
    /// Set true by ExperimentRunner during stress-mode trials. Suppresses incoming damage so
    /// the flock pooled HP never drains and SyncBoidCountToHealth never culls agents — the
    /// live agent count stays at the configured N for the full trial, giving the FPS
    /// benchmark a fixed compute load to compare across conditions.
    /// </summary>
    public bool SuspendDamage { get; set; }

    public void TakeDamage(float amount)
    {
        if (SuspendDamage) return;
        if (IsDead) return;
        currentFlockHealth = Mathf.Max(currentFlockHealth - amount, 0f);
        if (IsDead)
            KillAllBoids();
        else
            SyncBoidCountToHealth();
    }

    private void SyncBoidCountToHealth()
    {
        if (originalFlockSize == 0) return;

        // Scale live boid count from minSurvivorFraction..1 as health goes 0..1
        // Keeps the last minSurvivorFraction alive until health reaches 0
        float maxHP = EffectiveMaxHealth;
        float hp = maxHP > 0f ? currentFlockHealth / maxHP : 0f;
        int targetCount = Mathf.RoundToInt(
            Mathf.Lerp(settings.minSurvivorFraction, 1f, hp) * originalFlockSize);

        if (DiagnosticLogging && targetCount != lastLoggedTargetCount)
        {
            Debug.Log($"[FloorDiag/Cull] {name} t={Time.timeSinceLevelLoad:F2} " +
                      $"hp={currentFlockHealth:F0}/{maxHP:F0} ({hp * 100f:F0}%) " +
                      $"originalFlockSize={originalFlockSize} boidsCount={boids.Count} " +
                      $"targetCount={targetCount} delta={boids.Count - targetCount}");
            lastLoggedTargetCount = targetCount;
        }

        while (boids.Count > targetCount)
        {
            int last = boids.Count - 1;
            BoidAgent dying = boids[last];
            boids.RemoveAt(last);
            Destroy(dying.gameObject);
            // Player is the only damage source in a trial, so each cull is a kill
            // attributable to the player. Logged for AgentsKilledByPlayer trial metric.
            BehavioralMetricsCollector.Instance?.RecordAgentDeath();
        }

        // Cancel melee attack if too few boids remain
        if (meleePhase != MeleeAttackPhase.None && boids.Count < 2)
            ResetMeleeAttack();

        // Recompute formation if boids die mid-attack
        if (rangedPhase == RangedAttackPhase.Forming || rangedPhase == RangedAttackPhase.Locked)
        {
            if (boids.Count < 3)
            {
                ReleaseBoidFormations();
                rangedPhase = RangedAttackPhase.None;
            }
            else
            {
                ComputeFormationSlots(formationOrbitAngle);
                AssignFormationToBoids();
            }
        }
    }

    private void KillAllBoids()
    {
        if (DiagnosticLogging)
        {
            Debug.Log($"[FloorDiag/Wipe] {name} t={Time.timeSinceLevelLoad:F2} " +
                      $"originalFlockSize={originalFlockSize} boidsAtWipe={boids.Count} " +
                      $"hp={currentFlockHealth:F0}/{EffectiveMaxHealth:F0}");
        }
        for (int i = boids.Count - 1; i >= 0; i--)
            Destroy(boids[i].gameObject);
        boids.Clear();

        FlockCoordinator coordinator = FindFirstObjectByType<FlockCoordinator>();
        coordinator?.UnregisterFlock(this);
        Destroy(gameObject);
    }

    /// <summary>
    /// World point the flock's boundary steering is centered on. In the leader
    /// condition this is the leader's LIVE position, so followers are kept within
    /// boundaryRadius of the leader instead of the stale spawn point; otherwise it
    /// is the manager transform (PureBOIDS drifts that via MoveAnchor).
    ///
    /// This MUST stay a computed property — never assign it back to
    /// transform.position. The boids are parented to this transform, so writing
    /// the parent drags every child boid (the leader included), and chasing the
    /// leader's own position then becomes an exponential runaway.
    /// </summary>
    public Vector3 AnchorPosition =>
        leaderBoid != null && leaderBoid.gameObject != null
            ? leaderBoid.Position
            : transform.position;

    public float EffectiveBoundaryRadius
    {
        get
        {
            if (state != FlockState.Idle && settings.engageBoundaryRadius > 0f)
                return settings.engageBoundaryRadius;
            return settings.boundaryRadius;
        }
    }

    public float EffectiveAvoidanceRadius
    {
        get
        {
            if (state != FlockState.Idle && settings.engageAvoidanceRadius > 0f)
                return settings.engageAvoidanceRadius;
            return settings.avoidanceRadius;
        }
    }

    /// <summary>
    /// Read-only slot check used by GOAP sensors.
    /// Does NOT consume a slot — call RequestAttack() in the action's Start() to do that.
    /// </summary>
    public bool CanAttack => currentAttackerCount < settings.maxSimultaneousAttackers;

    public bool RequestAttack()
    {
        if (currentAttackerCount >= settings.maxSimultaneousAttackers)
            return false;
        currentAttackerCount++;
        return true;
    }

    public void ReleaseAttack()
    {
        currentAttackerCount = Mathf.Max(currentAttackerCount - 1, 0);
    }

    /// <summary>
    /// Called by ConditionManager BEFORE Start() fires (via AddComponent ordering)
    /// so the manager can spawn the requested flock size and HP pool for thesis
    /// parity. -1 on either field means "use settings value."
    /// </summary>
    public void ApplyComparisonOverrides(int flockSize, float maxHealth)
    {
        if (flockSize > 0) flockSizeOverride = flockSize;
        if (maxHealth > 0f) maxHealthOverride = maxHealth;
    }

    /// <summary>
    /// True when the state permits attack machines (melee wave + ranged volley)
    /// to advance. Only Engaging/Flanking permit both; Kiting permits ranged only
    /// (the caller checks flockType). All other states suppress attacks entirely.
    /// </summary>
    public bool AttackMachineAllowed
    {
        get
        {
            switch (state)
            {
                case FlockState.Engaging:
                case FlockState.Flanking:
                case FlockState.Kiting:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Max distance any boid sits from the flock centroid. Feeds the Regrouping
    /// transition (fires when this exceeds settings.isolationThreshold).
    /// </summary>
    public float GetMaxBoidDistanceFromCentroid()
    {
        if (boids.Count == 0) return 0f;
        Vector3 center = GetFlockCenter();
        float max = 0f;
        for (int i = 0; i < boids.Count; i++)
        {
            float d = Vector3.Distance(boids[i].Position, center);
            if (d > max) max = d;
        }
        return max;
    }

    public void SetTarget(Transform t)
    {
        bool wasNull = target == null;
        target = t;
        if (t != null)
        {
            state = FlockState.Grouping;
            // First target-acquisition of this trial is the stimulus event the
            // reaction-time metric measures against. Fires once per flock per trial.
            if (wasNull)
                BehavioralMetricsCollector.Instance?.LogEvent(
                    "StimulusAcquired", gameObject.name,
                    (int)settings.flockType, $"Target={t.name}");
        }
    }

    public void ClearTarget()
    {
        target = null;
        state = FlockState.Idle;
        ReleaseBoidFormations();
        rangedPhase = RangedAttackPhase.None;
        ResetMeleeAttack();
    }

    /// <summary>
    /// External hand-off for the BOIDSWithGOAPLeader condition. UpdateFlockState's
    /// resolver-driven transitions are gated off when UsesGoapForBoids — this method
    /// gives <see cref="LeaderGoapBrain"/> an explicit channel to drive flock state
    /// from its resolved goal so followers' per-state steering branches in
    /// <see cref="BoidAgent.UpdateBoid"/> (Engaging/Flanking/Scattering/Fleeing/Kiting/
    /// Guarding/Regrouping) actually fire instead of staying at Idle. Called every
    /// Update tick by the leader. State-change logging is intentionally omitted —
    /// LeaderGoapBrain already emits a GoalChange event for the leader's goal flip,
    /// which is the upstream cause of this state change; a second event for the same
    /// logical transition would clutter the EventLog.
    /// </summary>
    public void SetStateFromLeader(FlockState newState)
    {
        state = newState;
    }

    public void SetForeignBoids(List<BoidAgent> foreign)
    {
        foreignBoids = foreign;
    }

    private void Awake()
    {
        if (settings != null && settings.aggroRadius > 0f)
        {
            aggroTrigger = gameObject.AddComponent<SphereCollider>();
            aggroTrigger.radius = settings.aggroRadius;
            aggroTrigger.isTrigger = true;

            if (GetComponent<Rigidbody>() == null)
            {
                Rigidbody rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }
    }

    private void Start()
    {
        if (boidPrefab != null && settings != null)
        {
            if (settings.obstacleMask == 0)
                Debug.LogWarning($"BoidSettings '{settings.name}' has obstacleMask=0; obstacle avoidance disabled — boids will phase through geometry.");

            SpawnFlock();
            originalFlockSize = boids.Count;
            // Honour HP override before falling back to settings.maxHealth.
            currentFlockHealth = maxHealthOverride > 0f ? maxHealthOverride : settings.maxHealth;

            if (DiagnosticLogging)
            {
                Debug.Log($"[FloorDiag/Spawn] {name} type={settings.flockType} " +
                          $"flockSizeOverride={flockSizeOverride} originalFlockSize={originalFlockSize} " +
                          $"settingsFlockSize={settings.flockSize} " +
                          $"maxHealthOverride={maxHealthOverride:F0} effectiveMaxHP={EffectiveMaxHealth:F0} " +
                          $"settingsMaxHP={settings.maxHealth:F0} minSurvivor={settings.minSurvivorFraction:F2} " +
                          $"floorAtZero={Mathf.RoundToInt(settings.minSurvivorFraction * originalFlockSize)} " +
                          $"t={Time.timeSinceLevelLoad:F2}");
            }
        }
    }

    private void Update()
    {
        if (settings == null) return;

        // Leader-driven conditions (BOIDSWithGOAPLeader) suppress flock-level
        // decision making — the leader's GOAP owns everything.
        bool useGoap = ConditionManager.Instance != null && ConditionManager.Instance.UsesGoapForBoids;

        if (!useGoap)
            UpdateFlockState();

        if (target == null) return;

        // Attack machines gated by state — Fleeing/Scattering/Guarding/Regrouping/Grouping/Idle suppress.
        if (!useGoap && AttackMachineAllowed)
        {
            if (settings.flockType == FlockType.Ranged)
                UpdateRangedFlockAttack();
            if (settings.flockType == FlockType.Melee)
                UpdateMeleeFlockAttack();
        }

        // Anchor movement — PureBOIDS only. Chase target for Engaging/Flanking/
        // Guarding/Grouping, retreat for Fleeing/Kiting/Scattering, hold otherwise.
        // The leader condition does not move this transform at all; its boundary
        // steering reads the leader's live position via AnchorPosition instead.
        if (!useGoap)
            MoveAnchor();
    }

    private void MoveAnchor()
    {
        if (target == null) return;

        switch (state)
        {
            case FlockState.Engaging:
            case FlockState.Flanking:
            case FlockState.Grouping:
            {
                Vector3 direction = target.position - transform.position;
                float distance = direction.magnitude;
                if (distance < settings.targetStopDistance) return;
                float speed = settings.targetFollowSpeed;
                if (distance < settings.targetSlowDistance)
                {
                    float t = (distance - settings.targetStopDistance) / (settings.targetSlowDistance - settings.targetStopDistance);
                    speed *= Mathf.Clamp01(t);
                }
                transform.position = Vector3.MoveTowards(transform.position, target.position, speed * Time.deltaTime);
                break;
            }

            case FlockState.Guarding:
            {
                // Hold position at guardInnerRange — close the gap if beyond, back off if closer.
                float dist = Vector3.Distance(transform.position, target.position);
                Vector3 toTarget = (target.position - transform.position).normalized;
                float step = settings.targetFollowSpeed * 0.5f * Time.deltaTime;
                if (dist > settings.guardInnerRange + 1f)
                    transform.position += toTarget * step;
                else if (dist < settings.guardInnerRange - 1f)
                    transform.position -= toTarget * step;
                break;
            }

            case FlockState.Fleeing:
            case FlockState.Kiting:
            case FlockState.Scattering:
            {
                Vector3 awayDir = (transform.position - target.position).normalized;
                float speed = settings.targetFollowSpeed * settings.fleeSpeedMultiplier;
                transform.position += awayDir * speed * Time.deltaTime;
                break;
            }

            // Idle / Regrouping: hold anchor, let cohesion tighten the flock.
        }
    }

    /// <summary>
    /// Flock-level state transition. Mirrors GoalPriorityResolver priority via
    /// FlockStateResolver so PureBOIDS expresses the same behavior set that the
    /// GOAP conditions express through planning.
    /// </summary>
    private void UpdateFlockState()
    {
        // Scattering auto-expires so the flock can recover to Fleeing/Engaging/etc.
        if (state == FlockState.Scattering)
        {
            scatterTimer -= Time.deltaTime;
            if (scatterTimer > 0f) return; // stay scattering
        }

        float playerDist = target != null
            ? Vector3.Distance(target.position, transform.position)
            : float.MaxValue;
        bool hasTarget = target != null;
        bool playerNearby = hasTarget && playerDist <= settings.aggroRadius;

        // Clustered-enough check (existing Grouping → Engaging transition criterion).
        float effectiveRadius = EffectiveBoundaryRadius;
        float radiusSqr = effectiveRadius * effectiveRadius;
        int clusteredCount = 0;
        for (int i = 0; i < boids.Count; i++)
        {
            Vector3 offset = boids[i].Position - transform.position;
            if (offset.sqrMagnitude <= radiusSqr) clusteredCount++;
        }
        float clusterFraction = boids.Count > 0 ? (float)clusteredCount / boids.Count : 1f;
        bool clusteredEnough = clusterFraction >= settings.groupUpThreshold;

        float maxDistFromCentroid = GetMaxBoidDistanceFromCentroid();
        bool rangedActive = rangedPhase != RangedAttackPhase.None && rangedPhase != RangedAttackPhase.Recovering;
        bool meleeActive = meleePhase != MeleeAttackPhase.None && meleePhase != MeleeAttackPhase.Recovering;
        bool slotsSaturated = currentAttackerCount >= settings.maxSimultaneousAttackers;
        // Mirror of GOAP's !cooldownReady: the flock wave/volley is on its post-attack
        // cooldown and cannot engage. Feeding this into the Flanking rule closes the
        // parity gap with GoalPriorityResolver (Conditions 3/4).
        bool flockCooldownActive = flockMeleeCooldownTimer > 0f || flockAttackCooldownTimer > 0f;

        var behavior = FlockStateResolver.Resolve(
            healthPercent: HealthPercent,
            hasTarget: hasTarget,
            playerNearby: playerNearby,
            playerDist: playerDist,
            isRanged: settings.flockType == FlockType.Ranged,
            attackSlotsSaturated: slotsSaturated,
            meleeAttackActive: meleeActive,
            rangedAttackActive: rangedActive,
            maxDistanceFromCentroid: maxDistFromCentroid,
            boidCount: boids.Count,
            clusteredEnoughToEngage: clusteredEnough,
            criticalHealthThreshold: settings.criticalHealthThreshold,
            fleeHealthThreshold: settings.fleeHealthThreshold,
            kiteMinDistance: settings.kiteMinDistance,
            isolationThreshold: settings.isolationThreshold,
            guardInnerRange: settings.guardInnerRange,
            guardOuterRange: settings.guardOuterRange,
            flockCooldownActive: flockCooldownActive);

        // FlockBehavior and FlockState are declared in the same order so a cast is safe.
        FlockState newState = (FlockState)(int)behavior;

        // Minimum-dwell hysteresis: if we just changed state, don't flip again for
        // MinStateDwellSeconds. Scattering is exempt because it has its own timer
        // (and Scattering override always represents a critical-HP panic).
        bool allowTransition =
            newState == state
            || newState == FlockState.Scattering
            || Time.time - stateEnterTime >= MinStateDwellSeconds;
        if (!allowTransition)
        {
            return;
        }

        // Scattering entry: arm the timer and cancel in-flight attacks.
        if (newState == FlockState.Scattering && state != FlockState.Scattering)
        {
            scatterTimer = settings.scatterDuration;
            ResetMeleeAttack();
            if (rangedPhase != RangedAttackPhase.None)
            {
                ReleaseBoidFormations();
                rangedPhase = RangedAttackPhase.None;
            }
        }

        // Log state transitions as GoalChange events so PureBOIDS shows up in the
        // same reaction-time + event-log pipeline as GOAP conditions.
        if (newState != state)
        {
            BehavioralMetricsCollector.Instance?.LogEvent(
                "GoalChange", gameObject.name, (int)settings.flockType, $"{state}→{newState}");
            stateEnterTime = Time.time;
        }

        state = newState;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (target == null && settings != null && other.CompareTag(settings.aggroTag))
            SetTarget(other.transform);
    }

    public void SpawnFlock()
    {
        // Cache the GOAP behaviour lookup once, outside the per-boid loop.
        var goapBehaviour = FindFirstObjectByType<GoapBehaviour>();

        int flockSize = flockSizeOverride > 0 ? flockSizeOverride : settings.flockSize;
        for (int i = 0; i < flockSize; i++)
        {
            Vector3 spawnPos = transform.position + Random.insideUnitSphere * settings.spawnRadius;
            Vector3 startVelocity = Random.onUnitSphere * settings.maxSpeed * 0.5f;

            GameObject boidObj = Instantiate(boidPrefab, spawnPos, Quaternion.LookRotation(startVelocity), transform);
            BoidAgent agent = boidObj.GetComponent<BoidAgent>();
            agent.settings = settings;
            agent.manager = this;
            agent.subgroupId = i % ScatterSubgroupCount;
            agent.Initialize(startVelocity);
            ApplyFlockColor(agent);

            // Phase 11 — GOAP agent-type wiring.
            if (goapBehaviour != null)
            {
                var provider = agent.GetComponent<GoapActionProvider>();
                if (provider != null)
                {
                    string agentTypeName = settings.flockType == FlockType.Melee ? "MeleeBoid" : "RangedBoid";
                    provider.AgentType = goapBehaviour.GetAgentType(agentTypeName);
                }
            }

            boids.Add(agent);
        }
    }

    public void AddBoids(List<BoidAgent> incoming)
    {
        int baseIndex = boids.Count;
        for (int i = 0; i < incoming.Count; i++)
        {
            BoidAgent boid = incoming[i];
            boid.transform.SetParent(transform);
            boid.settings = settings;
            boid.manager = this;
            boid.subgroupId = (baseIndex + i) % ScatterSubgroupCount;
            ApplyFlockColor(boid);
            boids.Add(boid);
        }

        // Recompute formation if merging during an attack
        if (rangedPhase == RangedAttackPhase.Forming || rangedPhase == RangedAttackPhase.Locked)
        {
            ComputeFormationSlots(formationOrbitAngle);
            AssignFormationToBoids();
        }
    }

    public List<BoidAgent> RemoveBoids(int count)
    {
        count = Mathf.Min(count, boids.Count);
        List<BoidAgent> removed = new List<BoidAgent>(count);

        // Remove from the end to avoid shifting
        int startIndex = boids.Count - count;
        for (int i = boids.Count - 1; i >= startIndex; i--)
        {
            removed.Add(boids[i]);
            boids.RemoveAt(i);
        }

        return removed;
    }

    public void InitializeFromSplit(BoidSettings sourceSettings, List<BoidAgent> splitBoids)
    {
        settings = sourceSettings;
        AddBoids(splitBoids);
    }

    public Vector3 GetFlockCenter()
    {
        if (boids.Count == 0)
            return transform.position;

        Vector3 center = Vector3.zero;
        for (int i = 0; i < boids.Count; i++)
            center += boids[i].Position;

        return center / boids.Count;
    }

    // Centroid of all boids EXCLUDING the leader. Used by the leader leash so the
    // distance measurement reflects the bulk of the flock rather than being diluted
    // by the leader's own position. At small per-flock counts (e.g. ranged at N=50
    // → ~17 boids) the leader's 1/N contribution to GetFlockCenter() lets it drift
    // 25 m+ from the actual flock bulk while the leash still reads under 12 m.
    // Falls back to GetFlockCenter() when there is no leader or no followers.
    public Vector3 GetFollowerCentroid()
    {
        if (leaderBoid == null || boids.Count <= 1)
            return GetFlockCenter();
        int leaderIndex = boids.IndexOf(leaderBoid);
        return ComputeFollowerCentroid(boids.Count, leaderIndex, i => boids[i].Position, GetFlockCenter());
    }

    // Pure helper. Public-static so EditMode tests drive it directly without a scene
    // setup; the GetFollowerCentroid() instance method is the production caller.
    public static Vector3 ComputeFollowerCentroid(int count, int leaderIndex, System.Func<int, Vector3> positionAt, Vector3 fallback)
    {
        if (count <= 1 || leaderIndex < 0 || leaderIndex >= count) return fallback;
        Vector3 sum = Vector3.zero;
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            if (i == leaderIndex) continue;
            sum += positionAt(i);
            n++;
        }
        return n > 0 ? sum / n : fallback;
    }

    private void ApplyFlockColor(BoidAgent boid)
    {
        Renderer renderer = boid.GetComponentInChildren<Renderer>();
        if (renderer != null)
            renderer.material.color = settings.flockColor;
    }

    private void LateUpdate()
    {
        float perceptionSqr = settings.perceptionRadius * settings.perceptionRadius;
        float effectiveAvoidance = EffectiveAvoidanceRadius;
        float avoidanceSqr = effectiveAvoidance * effectiveAvoidance;

        bool doDebug = false;
        if (MovementDebug)
        {
            movementDebugTimer -= Time.deltaTime;
            if (movementDebugTimer <= 0f)
            {
                doDebug = true;
                movementDebugTimer = 0.5f;
            }
        }

        for (int i = 0; i < boids.Count; i++)
        {
            BoidAgent boid = boids[i];
            Vector3 separationHeading = Vector3.zero;
            Vector3 alignmentHeading = Vector3.zero;
            Vector3 cohesionCenter = Vector3.zero;
            int neighborCount = 0;

            for (int j = 0; j < boids.Count; j++)
            {
                if (i == j) continue;

                BoidAgent other = boids[j];
                Vector3 offset = other.Position - boid.Position;
                float sqrDist = offset.sqrMagnitude;

                if (sqrDist < perceptionSqr)
                {
                    neighborCount++;
                    alignmentHeading += other.Velocity;
                    cohesionCenter += other.Position;

                    if (sqrDist < avoidanceSqr)
                    {
                        // Weight separation inversely by distance
                        separationHeading -= offset / Mathf.Max(offset.magnitude, 0.001f);
                    }
                }
            }

            // Leader influence model (revised 2026-05-06): followers always run STANDARD
            // BOIDS cohesion (toward neighbor centroid) when they have neighbors. When a
            // leader exists, an additive directional bias toward a leader-relative follow
            // anchor (behind the leader when it moves, on the leader when it is still — see
            // the hasLeader block below) is layered on top, applied at unit length (NOT as
            // the raw displacement vector) so the bias
            // does not dominate the cohesion direction at long range. Without normalisation
            // a leader 30 m away with weight 0.3 produces a 9-unit addend that overwhelms a
            // 1-unit standard cohesion vector, defeating the "light influence" intent.
            //
            // Bias weight ramps with neighborCount: tightly-packed followers get a light
            // nudge (minLeaderWeight); stranded followers (zero neighbors) get the full pull
            // (maxLeaderWeight) so they can rejoin instead of drifting away. The smooth ramp
            // avoids the visual snap a discrete switch would produce as followers cross the
            // perception-radius boundary in dispersed combat (perceptionRadius is only 2.5 m
            // so this boundary is crossed often).
            //
            // BoidAgent.SteerTowards normalises the cohesion vector internally, so only the
            // direction of cohesionCenter matters downstream — these magnitudes are tuned
            // for which-side-wins-the-direction, not for force scale.
            const float strandedNeighborCount = 5f;   // approx. count in a packed flock
            const float minLeaderWeight       = 0.3f; // tightly packed
            const float maxLeaderWeight       = 1.0f; // stranded
            bool hasLeader = leaderBoid != null && boid != leaderBoid && leaderBoid.gameObject != null;

            if (neighborCount > 0)
            {
                alignmentHeading /= neighborCount;
                cohesionCenter = (cohesionCenter / neighborCount) - boid.Position;
            }
            // else: cohesionCenter stays Vector3.zero; the leader bias below is the only
            // cohesion source for stranded followers.

            if (hasLeader)
            {
                // Followers trail BEHIND a moving leader and gather AROUND a near-
                // stationary one. The follow anchor is the leader's position pushed
                // backward along its heading; the offset scales with leader speed, so
                // it shrinks to zero as the leader slows — cohesion plus mutual
                // separation then settle the flock into a loose ring around the leader.
                // trailDistance is the offset at full leader speed; raise it for a
                // longer tail, lower it to keep followers tucked in close.
                const float trailDistance = 4f;
                Vector3 leaderVel   = leaderBoid.Velocity;
                float   leaderSpeed = leaderVel.magnitude;

                Vector3 followAnchor = leaderBoid.Position;
                if (leaderSpeed > 0.01f)
                {
                    float trailLerp = Mathf.Clamp01(leaderSpeed / settings.maxSpeed);
                    followAnchor -= (leaderVel / leaderSpeed) * (trailDistance * trailLerp);
                }

                Vector3 toAnchor = followAnchor - boid.Position;
                if (toAnchor.sqrMagnitude > 0.000001f)
                {
                    float strandedness = Mathf.Clamp01(1f - neighborCount / strandedNeighborCount);
                    float weight       = Mathf.Lerp(minLeaderWeight, maxLeaderWeight, strandedness);
                    cohesionCenter    += weight * toAnchor.normalized;
                }
            }

            // Cross-flock separation (separation only, no alignment/cohesion)
            Vector3 crossFlockSeparation = Vector3.zero;
            for (int f = 0; f < foreignBoids.Count; f++)
            {
                Vector3 offset = foreignBoids[f].Position - boid.Position;
                float sqrDist = offset.sqrMagnitude;

                if (sqrDist < avoidanceSqr)
                {
                    crossFlockSeparation -= offset / Mathf.Max(offset.magnitude, 0.001f);
                }
            }

            // Isolate per-boid failures: an exception thrown inside one boid's
            // UpdateBoid must not abort the loop and freeze every boid after it.
            // The error is still logged so it stays diagnosable, never silent.
            try
            {
                boid.UpdateBoid(separationHeading, alignmentHeading, cohesionCenter, neighborCount, crossFlockSeparation, doDebug && i == 0);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"BoidAgent.UpdateBoid failed for '{boid.name}': {e}", boid);
            }
        }

        if (doDebug)
        {
            float avgSpeed = 0f;
            for (int i = 0; i < boids.Count; i++)
                avgSpeed += boids[i].Velocity.magnitude;
            if (boids.Count > 0) avgSpeed /= boids.Count;
            Debug.Log(
                $"[BoidDebug] FLOCK {name} type={settings.flockType} state={state} boids={boids.Count}" +
                $" target={(target != null ? target.name : "none")} avgSpeed={avgSpeed:F2}" +
                $" center={GetFlockCenter()} anchor={transform.position}",
                this);
        }

        // Diagnostic: leader-goal-change → first-follower-cohesion-cycle propagation.
        // pendingFollowerReactTime is stamped by LeaderGoapBrain on the same frame
        // the leader's goal changes; this LateUpdate is the first opportunity for
        // followers to feel the leader's new trajectory via the cohesion redirect
        // above (lines 666-694). One event per goal change, then re-arm with -1f.
        if (LeaderGoapBrain.DiagnosticLogging
            && pendingFollowerReactTime >= 0f
            && leaderBoid != null
            && boids.Count > 1)
        {
            float delta = (Time.time - pendingFollowerReactTime) * 1000f;
            BehavioralMetricsCollector.Instance?.LogEvent(
                "FollowerReact", gameObject.name, (int)settings.flockType,
                $"Followers={boids.Count - 1},DeltaMs={delta:F1}");
            pendingFollowerReactTime = -1f;
        }
    }

    // ── Flock Ranged Attack ──────────────────────────────────────────

    private void UpdateRangedFlockAttack()
    {
        if (flockAttackCooldownTimer > 0f)
            flockAttackCooldownTimer -= Time.deltaTime;

        switch (rangedPhase)
        {
            case RangedAttackPhase.None:
            {
                if (flockAttackCooldownTimer > 0f || boids.Count < 3) break;

                float dist = (target.position - transform.position).magnitude;
                if (dist <= settings.flockAttackTriggerDistance)
                {
                    rangedPhase = RangedAttackPhase.Forming;
                    rangedPhaseTimer = 0f;
                    formationOrbitAngle = 0f;
                    ComputeFormationSlots(0f);
                    AssignFormationToBoids();
                }
                break;
            }

            case RangedAttackPhase.Forming:
            {
                // Recompute slots each frame so ring stays at flock anchor
                ComputeFormationSlots(0f);
                AssignFormationToBoids();

                // Check convergence
                int onSlot = 0;
                for (int i = 0; i < boids.Count; i++)
                {
                    int slotIndex = i % formationSlots.Length;
                    float d = (boids[i].Position - formationSlots[slotIndex]).magnitude;
                    if (d <= settings.formationSlotTolerance)
                        onSlot++;
                }

                float fraction = boids.Count > 0 ? (float)onSlot / boids.Count : 0f;

                // Safety timeout: 6 seconds max for forming
                rangedPhaseTimer += Time.deltaTime;
                if (fraction >= settings.formationConvergeThreshold || rangedPhaseTimer > 6f)
                {
                    rangedPhase = RangedAttackPhase.Locked;
                    rangedPhaseTimer = settings.formationDuration;
                }
                break;
            }

            case RangedAttackPhase.Locked:
            {
                formationOrbitAngle += settings.formationOrbitSpeed * Time.deltaTime;
                ComputeFormationSlots(formationOrbitAngle);
                AssignFormationToBoids();

                rangedPhaseTimer -= Time.deltaTime;
                if (rangedPhaseTimer <= 0f)
                {
                    rangedPhase = RangedAttackPhase.Firing;
                }
                break;
            }

            case RangedAttackPhase.Firing:
            {
                // Spawn a single projectile at the ring center aimed at the player
                if (settings.projectilePrefab != null && target != null)
                {
                    Vector3 center = transform.position;
                    Vector3 dir = (target.position - center).normalized;
                    GameObject proj = Object.Instantiate(
                        settings.projectilePrefab,
                        center,
                        Quaternion.LookRotation(dir)
                    );
                    BoidProjectile bp = proj.GetComponent<BoidProjectile>();
                    bp?.Initialize(dir, settings.projectileSpeed, settings.flockProjectileDamage, target, settings.projectileTurnSpeed);
                }

                // Knockback — push boids outward from ring center
                for (int i = 0; i < boids.Count; i++)
                {
                    Vector3 outward = (boids[i].Position - transform.position).normalized;
                    boids[i].ApplyKnockback(outward * settings.formationKnockbackForce);
                }

                ReleaseBoidFormations();
                rangedPhase = RangedAttackPhase.Recovering;
                rangedPhaseTimer = 0.5f;
                break;
            }

            case RangedAttackPhase.Recovering:
            {
                rangedPhaseTimer -= Time.deltaTime;
                if (rangedPhaseTimer <= 0f)
                {
                    rangedPhase = RangedAttackPhase.None;
                    flockAttackCooldownTimer = settings.flockAttackCooldown;
                }
                break;
            }
        }
    }

    private void ComputeFormationSlots(float orbitOffset = 0f)
    {
        int count = boids.Count;
        if (count == 0) return;

        formationSlots = new Vector3[count];
        float angleStep = 360f / count;
        Vector3 center = transform.position;

        // Build a vertical ring that faces the player
        Vector3 toTarget = target != null ? target.position - center : transform.forward;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f) toTarget = transform.forward;
        toTarget.Normalize();

        Vector3 ringRight = Vector3.Cross(Vector3.up, toTarget).normalized;
        Vector3 ringUp = Vector3.up;

        for (int i = 0; i < count; i++)
        {
            float angle = (angleStep * i + orbitOffset) * Mathf.Deg2Rad;
            formationSlots[i] = center
                + ringRight * Mathf.Cos(angle) * settings.formationRingRadius
                + ringUp * Mathf.Sin(angle) * settings.formationRingRadius;
        }
    }

    private void AssignFormationToBoids()
    {
        if (formationSlots == null) return;
        for (int i = 0; i < boids.Count; i++)
        {
            int slotIndex = i % formationSlots.Length;
            boids[i].SetFormationTarget(formationSlots[slotIndex], true);
        }
    }

    private void ReleaseBoidFormations()
    {
        for (int i = 0; i < boids.Count; i++)
            boids[i].SetFormationTarget(Vector3.zero, false);
    }

    // ── Flock Melee Attack ────────────────────────────────────────

    private void UpdateMeleeFlockAttack()
    {
        if (flockMeleeCooldownTimer > 0f)
            flockMeleeCooldownTimer -= Time.deltaTime;

        switch (meleePhase)
        {
            case MeleeAttackPhase.None:
            {
                if (flockMeleeCooldownTimer > 0f || boids.Count < 2) break;

                float dist = (target.position - transform.position).magnitude;
                if (dist <= settings.meleeFlockTriggerDistance)
                {
                    meleePhase = MeleeAttackPhase.WindUp;
                    meleePhaseTimer = settings.attackWindUpDuration;
                    meleeWaveDamageDealt = false;
                    for (int i = 0; i < boids.Count; i++)
                        boids[i].BeginMeleeWindUp();
                }
                break;
            }

            case MeleeAttackPhase.WindUp:
            {
                meleePhaseTimer -= Time.deltaTime;
                if (meleePhaseTimer <= 0f)
                {
                    meleePhase = MeleeAttackPhase.Charging;
                    meleePhaseTimer = settings.attackSweepDuration;
                    for (int i = 0; i < boids.Count; i++)
                        boids[i].BeginInfinitySweep();
                }
                break;
            }

            case MeleeAttackPhase.Charging:
            {
                if (!meleeWaveDamageDealt && target != null)
                {
                    for (int i = 0; i < boids.Count; i++)
                    {
                        float dist = (target.position - boids[i].Position).magnitude;
                        if (dist <= settings.attackContactDistance)
                        {
                            target.GetComponent<PlayerHealth>()?.TakeDamage(settings.attackDamage);
                            meleeWaveDamageDealt = true;
                            break;
                        }
                    }
                }

                meleePhaseTimer -= Time.deltaTime;
                if (meleePhaseTimer <= 0f)
                {
                    meleePhase = MeleeAttackPhase.Recovering;
                    meleePhaseTimer = settings.meleeRecoveryDuration;
                    for (int i = 0; i < boids.Count; i++)
                        boids[i].EndMeleeAttack();
                }
                break;
            }

            case MeleeAttackPhase.Recovering:
            {
                meleePhaseTimer -= Time.deltaTime;
                if (meleePhaseTimer <= 0f)
                {
                    meleePhase = MeleeAttackPhase.None;
                    flockMeleeCooldownTimer = settings.meleeFlockCooldown;
                }
                break;
            }
        }
    }

    private void ResetMeleeAttack()
    {
        if (meleePhase == MeleeAttackPhase.None) return;
        for (int i = 0; i < boids.Count; i++)
            boids[i].EndMeleeAttack();
        meleePhase = MeleeAttackPhase.None;
    }

    // ── Gizmos ──────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!drawGizmos || settings == null) return;

        // Boundary sphere
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, EffectiveBoundaryRadius);

        // Spawn area
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, settings.spawnRadius);

        // Aggro radius
        if (settings.aggroRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, settings.aggroRadius);
        }

        // Formation ring (during ranged attack)
        if (rangedPhase != RangedAttackPhase.None && settings.flockType == FlockType.Ranged)
        {
            Gizmos.color = rangedPhase == RangedAttackPhase.Locked
                ? new Color(1f, 0.5f, 0f, 0.6f)
                : new Color(0f, 0.8f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, settings.formationRingRadius);

            if (formationSlots != null)
            {
                for (int i = 0; i < formationSlots.Length; i++)
                    Gizmos.DrawSphere(formationSlots[i], 0.3f);
            }
        }
    }
}
