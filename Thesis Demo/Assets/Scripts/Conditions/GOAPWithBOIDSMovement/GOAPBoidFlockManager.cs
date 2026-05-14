using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Condition 4 (GOAPWithBOIDSMovement) flock-level glue.
/// Mirrors PureBOIDS FlockManager but layers ON TOP of per-agent GOAP brains:
///
///   - Pooled HP: damage hits the flock, not individuals. SyncBoidCountToHealth
///     despawns tail agents as HP drops (same formula as FlockManager.SyncBoidCountToHealth).
///   - Centroid: cached once per LateUpdate. Used for leash-pull + by sensors/actions.
///   - Leash: agents drifting beyond LeashRadius are pulled back via a force they
///     add themselves in ComputeBoidsForces (reads manager.Centroid).
///   - Ranged attack phases: Forming -> Locked -> Firing -> Recovering. During
///     Forming/Locked, manager writes a formation-ring slot onto each agent; the
///     ranged GOAP action reads that slot and steers to it instead of its own orbit.
///   - Melee attack phases: WindUp -> Charging -> Recovering. The melee GOAP action
///     reads the phase and fast-forwards to Charge when the flock wave is live.
///
/// One manager GameObject per flock (melee + ranged get their own), created by
/// ConditionManager.SpawnGOAPWithBOIDSMovementFlocks.
/// </summary>
public class GOAPBoidFlockManager : MonoBehaviour
{
    public enum RangedAttackPhase { None, Forming, Locked, Firing, Recovering }
    public enum MeleeAttackPhase { None, WindUp, Charging, Recovering }

    [Header("Identity")]
    [SerializeField] private FlockType flockType = FlockType.Melee;
    [SerializeField] private int flockId;

    [Header("Pooled Health")]
    // Sized for the worst-case: every agent in a clustered flock takes the same
    // player swing independently and each call deducts from this pool, so an AOE
    // catching N agents does N * damage. 3000 HP keeps the flock alive through
    // enough rotations for GOAP states to visibly trigger.
    [SerializeField] private float maxFlockHealth = 3000f;
    [Tooltip("Fraction of the original flock kept alive as last stand until HP hits 0")]
    [SerializeField, Range(0f, 1f)] private float minSurvivorFraction = 0.35f;

    [Header("Centroid Leash")]
    [Tooltip("Agents drifting farther than this from the flock centroid are pulled back")]
    [SerializeField] private float leashRadius = 15f;
    [SerializeField] private float leashStrength = 2f;

    [Header("Ranged Attack (Formation Volley)")]
    [SerializeField] private float flockAttackTriggerDistance = 14f;
    [SerializeField] private float formationRingRadius = 8f;
    [SerializeField] private float formationDuration = 1.5f;
    [SerializeField, Range(0f, 1f)] private float formationConvergeThreshold = 0.6f;
    [SerializeField] private float formationSlotTolerance = 2f;
    [SerializeField] private float formationOrbitSpeed = 60f;
    [SerializeField] private float flockAttackCooldown = 5f;

    [Header("Melee Attack (Synchronized Wave)")]
    [SerializeField] private float meleeFlockTriggerDistance = 8f;
    [SerializeField] private float meleeWindUpDuration = 0.4f;
    [SerializeField] private float meleeChargeDuration = 0.6f;
    [SerializeField] private float meleeRecoveryDuration = 0.5f;
    [SerializeField] private float meleeFlockCooldown = 4f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // Runtime state
    private readonly List<GOAPBoidAgent> agents = new List<GOAPBoidAgent>();
    private readonly List<GOAPBoidAgent> rangedParticipants = new List<GOAPBoidAgent>();
    private Transform target;
    private int originalFlockSize;
    private float currentFlockHealth;
    private Vector3 cachedCentroid;

    private RangedAttackPhase rangedPhase = RangedAttackPhase.None;
    private float rangedPhaseTimer;
    private float rangedCooldownTimer;
    private Vector3[] formationSlots;
    private float formationOrbitAngle;

    private MeleeAttackPhase meleePhase = MeleeAttackPhase.None;
    private float meleePhaseTimer;
    private float meleeCooldownTimer;

    // Public API
    public FlockType FlockType => flockType;
    public int FlockId => flockId;
    public int BoidCount => agents.Count;
    public float HealthPercent => maxFlockHealth > 0f ? currentFlockHealth / maxFlockHealth : 0f;
    public bool IsDead => currentFlockHealth <= 0f;

    /// <summary>
    /// Set true by ExperimentRunner during stress-mode trials. Suppresses incoming damage so
    /// pooled HP never drains and live agent count stays at configured N for the full trial.
    /// See [[methodology-revisions-2026-04]] item 1 for context.
    /// </summary>
    public bool SuspendDamage { get; set; }
    public Vector3 Centroid => cachedCentroid;
    public float LeashRadius => leashRadius;
    public float LeashStrength => leashStrength;
    public IReadOnlyList<GOAPBoidAgent> Agents => agents;
    public RangedAttackPhase CurrentRangedPhase => rangedPhase;
    public MeleeAttackPhase CurrentMeleePhase => meleePhase;
    public Transform Target => target;

    /// <summary>
    /// One-shot configuration from ConditionManager. Call BEFORE registering agents.
    /// </summary>
    public void Configure(FlockType type, int id, int expectedSize)
    {
        flockType = type;
        flockId = id;
        originalFlockSize = expectedSize;
        currentFlockHealth = maxFlockHealth;
        cachedCentroid = transform.position;
    }

    public void RegisterAgent(GOAPBoidAgent agent)
    {
        if (agent == null || agents.Contains(agent)) return;
        agent.subgroupId = agents.Count % FlockManager.ScatterSubgroupCount;
        agents.Add(agent);
        agent.flockManager = this;
        // Fallback: if Configure wasn't called (e.g. test harness), keep flock size in sync.
        if (originalFlockSize < agents.Count)
            originalFlockSize = agents.Count;
    }

    public void UnregisterAgent(GOAPBoidAgent agent)
    {
        agents.Remove(agent);
        rangedParticipants.Remove(agent);
    }

    public void NotifyTargetAcquired(Transform t)
    {
        if (target == null && t != null)
        {
            target = t;
            // First target-acquisition fires the stimulus event used by the
            // reaction-time metric in BehavioralMetricsCollector.
            BehavioralMetricsCollector.Instance?.LogEvent(
                "StimulusAcquired", gameObject.name, flockId, $"Target={t.name}");
        }
    }

    public void ClearTarget()
    {
        target = null;
        ReleaseFormations();
        rangedPhase = RangedAttackPhase.None;
        meleePhase = MeleeAttackPhase.None;
    }

    // ── Pooled damage ───────────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        if (SuspendDamage) return;
        if (IsDead) return;
        currentFlockHealth = Mathf.Max(currentFlockHealth - amount, 0f);
        if (IsDead)
            KillAllAgents();
        else
            SyncBoidCountToHealth();
    }

    private void SyncBoidCountToHealth()
    {
        if (originalFlockSize == 0) return;

        float hp = currentFlockHealth / maxFlockHealth;
        int targetCount = Mathf.RoundToInt(
            Mathf.Lerp(minSurvivorFraction, 1f, hp) * originalFlockSize);

        while (agents.Count > targetCount)
        {
            int last = agents.Count - 1;
            GOAPBoidAgent dying = agents[last];
            agents.RemoveAt(last);
            rangedParticipants.Remove(dying);
            if (dying != null)
                Destroy(dying.gameObject);
            // Player is the only damage source in a trial, so each cull is a kill
            // attributable to the player. Logged for AgentsKilledByPlayer trial metric.
            BehavioralMetricsCollector.Instance?.RecordAgentDeath();
        }

        if (rangedPhase == RangedAttackPhase.Forming || rangedPhase == RangedAttackPhase.Locked)
        {
            if (agents.Count < 3)
            {
                ReleaseFormations();
                rangedPhase = RangedAttackPhase.None;
            }
            else
            {
                ComputeFormationSlots(formationOrbitAngle);
                AssignFormationToBoids();
            }
        }

        if (meleePhase != MeleeAttackPhase.None && agents.Count < 2)
        {
            meleePhase = MeleeAttackPhase.None;
        }
    }

    private void KillAllAgents()
    {
        for (int i = agents.Count - 1; i >= 0; i--)
        {
            if (agents[i] != null)
                Destroy(agents[i].gameObject);
        }
        agents.Clear();
        rangedParticipants.Clear();
    }

    // ── Ranged attack participation ─────────────────────────────────

    public void RegisterRangedParticipant(GOAPBoidAgent agent)
    {
        if (agent == null || rangedParticipants.Contains(agent)) return;
        rangedParticipants.Add(agent);
    }

    public void UnregisterRangedParticipant(GOAPBoidAgent agent)
    {
        rangedParticipants.Remove(agent);
        if (agent != null)
        {
            agent.FlockFormationActive = false;
            agent.FlockFormationTarget = Vector3.zero;
        }
    }

    // ── Frame update ────────────────────────────────────────────────

    private void LateUpdate()
    {
        UpdateCentroid();

        if (target == null) return;

        if (flockType == FlockType.Ranged)
            UpdateRangedFlockAttack();
        else
            UpdateMeleeFlockAttack();
    }

    private void UpdateCentroid()
    {
        if (agents.Count == 0)
        {
            cachedCentroid = transform.position;
            return;
        }
        Vector3 sum = Vector3.zero;
        int n = 0;
        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i] == null) continue;
            sum += agents[i].Position;
            n++;
        }
        cachedCentroid = n > 0 ? sum / n : transform.position;
        transform.position = cachedCentroid;
    }

    // ── Ranged phase state machine ──────────────────────────────────

    private void UpdateRangedFlockAttack()
    {
        if (rangedCooldownTimer > 0f)
            rangedCooldownTimer -= Time.deltaTime;

        switch (rangedPhase)
        {
            case RangedAttackPhase.None:
            {
                if (rangedCooldownTimer > 0f) break;
                if (rangedParticipants.Count < 3) break;

                float dist = Vector3.Distance(target.position, cachedCentroid);
                if (dist <= flockAttackTriggerDistance)
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
                ComputeFormationSlots(0f);
                AssignFormationToBoids();

                int onSlot = 0;
                for (int i = 0; i < rangedParticipants.Count; i++)
                {
                    if (rangedParticipants[i] == null) continue;
                    int slotIndex = i % formationSlots.Length;
                    float d = Vector3.Distance(rangedParticipants[i].Position, formationSlots[slotIndex]);
                    if (d <= formationSlotTolerance)
                        onSlot++;
                }

                float fraction = rangedParticipants.Count > 0
                    ? (float)onSlot / rangedParticipants.Count
                    : 0f;

                rangedPhaseTimer += Time.deltaTime;
                if (fraction >= formationConvergeThreshold || rangedPhaseTimer > 6f)
                {
                    rangedPhase = RangedAttackPhase.Locked;
                    rangedPhaseTimer = formationDuration;
                }
                break;
            }

            case RangedAttackPhase.Locked:
            {
                formationOrbitAngle += formationOrbitSpeed * Time.deltaTime;
                ComputeFormationSlots(formationOrbitAngle);
                AssignFormationToBoids();

                rangedPhaseTimer -= Time.deltaTime;
                if (rangedPhaseTimer <= 0f)
                    rangedPhase = RangedAttackPhase.Firing;
                break;
            }

            case RangedAttackPhase.Firing:
            {
                // Per-agent projectiles preserve GOAP-ness: each ranged action spawns
                // its own projectile when it sees phase == Firing. The manager just
                // flips the phase and releases formations.
                ReleaseFormations();
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
                    rangedCooldownTimer = flockAttackCooldown;
                }
                break;
            }
        }
    }

    private void ComputeFormationSlots(float orbitOffset)
    {
        int count = Mathf.Max(rangedParticipants.Count, 1);
        if (formationSlots == null || formationSlots.Length != count)
            formationSlots = new Vector3[count];

        Vector3 center = cachedCentroid;
        Vector3 toTarget = target != null ? target.position - center : transform.forward;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f) toTarget = transform.forward;
        toTarget.Normalize();

        Vector3 ringRight = Vector3.Cross(Vector3.up, toTarget).normalized;
        Vector3 ringUp = Vector3.up;

        float angleStep = 360f / count;
        for (int i = 0; i < count; i++)
        {
            float angle = (angleStep * i + orbitOffset) * Mathf.Deg2Rad;
            formationSlots[i] = center
                + ringRight * Mathf.Cos(angle) * formationRingRadius
                + ringUp * Mathf.Sin(angle) * formationRingRadius;
        }
    }

    private void AssignFormationToBoids()
    {
        if (formationSlots == null) return;
        for (int i = 0; i < rangedParticipants.Count; i++)
        {
            GOAPBoidAgent a = rangedParticipants[i];
            if (a == null) continue;
            int slotIndex = i % formationSlots.Length;
            a.FlockFormationTarget = formationSlots[slotIndex];
            a.FlockFormationActive = true;
        }
    }

    private void ReleaseFormations()
    {
        for (int i = 0; i < rangedParticipants.Count; i++)
        {
            GOAPBoidAgent a = rangedParticipants[i];
            if (a == null) continue;
            a.FlockFormationActive = false;
        }
    }

    // ── Melee phase state machine ───────────────────────────────────

    private void UpdateMeleeFlockAttack()
    {
        if (meleeCooldownTimer > 0f)
            meleeCooldownTimer -= Time.deltaTime;

        switch (meleePhase)
        {
            case MeleeAttackPhase.None:
            {
                if (meleeCooldownTimer > 0f) break;
                if (agents.Count < 2) break;

                float dist = Vector3.Distance(target.position, cachedCentroid);
                if (dist <= meleeFlockTriggerDistance)
                {
                    meleePhase = MeleeAttackPhase.WindUp;
                    meleePhaseTimer = meleeWindUpDuration;
                }
                break;
            }

            case MeleeAttackPhase.WindUp:
                meleePhaseTimer -= Time.deltaTime;
                if (meleePhaseTimer <= 0f)
                {
                    meleePhase = MeleeAttackPhase.Charging;
                    meleePhaseTimer = meleeChargeDuration;
                }
                break;

            case MeleeAttackPhase.Charging:
                meleePhaseTimer -= Time.deltaTime;
                if (meleePhaseTimer <= 0f)
                {
                    meleePhase = MeleeAttackPhase.Recovering;
                    meleePhaseTimer = meleeRecoveryDuration;
                }
                break;

            case MeleeAttackPhase.Recovering:
                meleePhaseTimer -= Time.deltaTime;
                if (meleePhaseTimer <= 0f)
                {
                    meleePhase = MeleeAttackPhase.None;
                    meleeCooldownTimer = meleeFlockCooldown;
                }
                break;
        }
    }

    // ── Gizmos ──────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // Leash ring
        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.3f);
        Gizmos.DrawWireSphere(cachedCentroid, leashRadius);

        // Formation ring (ranged)
        if (flockType == FlockType.Ranged && rangedPhase != RangedAttackPhase.None)
        {
            Gizmos.color = rangedPhase == RangedAttackPhase.Locked
                ? new Color(1f, 0.5f, 0f, 0.6f)
                : new Color(0f, 0.8f, 1f, 0.4f);
            Gizmos.DrawWireSphere(cachedCentroid, formationRingRadius);
            if (formationSlots != null)
            {
                for (int i = 0; i < formationSlots.Length; i++)
                    Gizmos.DrawSphere(formationSlots[i], 0.3f);
            }
        }
    }
}
