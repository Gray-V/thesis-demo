using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Condition 4: GOAP with BOIDS Movement.
/// Individual GOAP planning per agent (attack, flee, wander decisions).
/// BOIDS steering forces (separation, alignment, cohesion) always active as movement layer.
///
/// Key difference from PureGOAP: cohesion + alignment are always-on reactive forces here,
/// not planned actions. GOAP only decides high-level goals; BOIDS forces handle group coordination.
/// </summary>
public class GOAPBoidAgent : MonoBehaviour, IEnemy
{
    public static readonly List<GOAPBoidAgent> AllAgents = new List<GOAPBoidAgent>();

    // Per-flock swarm cooldown — each flock volleys independently
    private static readonly Dictionary<int, float> swarmCooldownByFlock = new Dictionary<int, float>();

    [Header("Swarm Attack")]
    [SerializeField] private float swarmCooldownDuration = 3f;
    [SerializeField] private int maxSimultaneousAttackers = 3;
    public float SwarmCooldownDuration => swarmCooldownDuration;

    // Per-flock attack slot tracking
    private static readonly Dictionary<int, int> currentAttackersByFlock = new Dictionary<int, int>();
    private static readonly Dictionary<int, int> maxAttackersByFlock = new Dictionary<int, int>();

    // Tracks the frame on which each flock's swarm cooldown was last decremented.
    // Lets any agent in the flock claim the tick for a frame, without scanning the
    // AllAgents list to decide who goes first (was O(n) per agent; now O(1)).
    private static readonly Dictionary<int, int> lastSwarmCooldownTickFrame = new Dictionary<int, int>();

    public static void ResetAttackSlots()
    {
        currentAttackersByFlock.Clear();
        maxAttackersByFlock.Clear();
        swarmCooldownByFlock.Clear();
        lastSwarmCooldownTickFrame.Clear();
    }

    /// <summary>
    /// Returns true if the flock's swarm cooldown has expired.
    /// </summary>
    public static bool IsFlockCooldownReady(int flockId)
    {
        return !swarmCooldownByFlock.TryGetValue(flockId, out float cd) || cd <= 0f;
    }

    /// <summary>
    /// Sets the swarm cooldown for a specific flock.
    /// </summary>
    public static void SetFlockCooldown(int flockId, float duration)
    {
        swarmCooldownByFlock[flockId] = duration;
    }

    public static bool CanAttack(int flockId)
    {
        if (!IsFlockCooldownReady(flockId))
            return false;
        int current = currentAttackersByFlock.TryGetValue(flockId, out int c) ? c : 0;
        int max = maxAttackersByFlock.TryGetValue(flockId, out int m) ? m : 3;
        return current < max;
    }

    public static bool RequestAttackSlot(int flockId)
    {
        if (!IsFlockCooldownReady(flockId))
            return false;
        int current = currentAttackersByFlock.TryGetValue(flockId, out int c) ? c : 0;
        int max = maxAttackersByFlock.TryGetValue(flockId, out int m) ? m : 3;
        if (current >= max)
            return false;
        currentAttackersByFlock[flockId] = current + 1;
        return true;
    }

    public static void ReleaseAttackSlot(int flockId)
    {
        if (currentAttackersByFlock.TryGetValue(flockId, out int c))
            currentAttackersByFlock[flockId] = Mathf.Max(c - 1, 0);
    }

    [Header("Movement Settings")]
    [SerializeField] private float maxSpeed = 8f;
    [SerializeField] private float minSpeed = 2f;
    [SerializeField] private float maxSteerForce = 5f;
    [SerializeField] private float rotationSpeed = 5f;

    [Header("Obstacle Avoidance")]
    [SerializeField] private float obstacleAvoidanceRadius = 1.5f;
    [SerializeField] private float perceptionRadius = 10f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Combat")]
    [SerializeField] private float maxHealth = 50f;
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackRange = 5f;
    [SerializeField] private float attackCooldown = 2f;
    [SerializeField] private AttackType attackType = AttackType.Melee;
    [SerializeField] private GameObject projectilePrefab;

    [Header("BOIDS Forces (Always Active)")]
    [SerializeField] private float separationRadius = 4f;
    [SerializeField] private float separationWeight = 3f;
    [SerializeField] private float alignmentWeight = 2f;
    [SerializeField] private float cohesionWeight = 2f;

    [Header("Room Bounds")]
    [SerializeField] private float boundaryMargin = 10f;

    [Header("Swimming/Bobbing")]
    [SerializeField] private float bobAmplitude = 1.5f;
    [SerializeField] private float bobFrequency = 1.2f;
    [SerializeField] private float weaveAmplitude = 1.2f;
    [SerializeField] private float weaveFrequency = 0.8f;

    [Header("Appearance")]
    [SerializeField] private Color agentColor = Color.cyan;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = false;

    // Public state (accessed by GOAP actions)
    [HideInInspector] public Vector3 velocity;
    [HideInInspector] public Transform cachedTransform;
    [HideInInspector] public float cooldownTimer;
    [HideInInspector] public Transform targetPlayer;
    [HideInInspector] public bool isScattering;
    [HideInInspector] public int flockId;

    // Subgroup bucket for Scatter — set by GOAPBoidFlockManager.RegisterAgent so
    // ScatterDirectionPicker can partition the swarm into a few coherent dispersal
    // directions rather than each agent picking independently. Mirrors
    // FlockManager.ScatterSubgroupCount (Condition 2/3).
    [HideInInspector] public int subgroupId;

    // Flock-level glue (set by GOAPBoidFlockManager when condition 4 is spawned).
    // Null in tests that instantiate bare agents — all manager paths null-check.
    [HideInInspector] public GOAPBoidFlockManager flockManager;
    // Formation slot written by the manager during ranged Forming/Locked phases;
    // the ranged GOAP action steers to this when FlockFormationActive is true.
    [HideInInspector] public Vector3 FlockFormationTarget;
    [HideInInspector] public bool FlockFormationActive;

    private float currentHealth;
    private bool isDead;
    private RoomBounds roomBounds;
    private float bobPhaseOffset;
    private float weavePhaseOffset;
    private Rigidbody rb;

    // IEnemy interface
    public bool IsDead => isDead;
    public float HealthPercent => flockManager != null ? flockManager.HealthPercent : currentHealth / maxHealth;
    public Vector3 Position => cachedTransform.position;
    public Vector3 Velocity => velocity;

    public float MaxSpeed => maxSpeed;
    public float AttackRange => attackRange;
    public float AttackDamage => attackDamage;
    public float AttackCooldown => attackCooldown;
    public AttackType AgentAttackType => attackType;
    public GameObject ProjectilePrefab => projectilePrefab;
    public void SetAttackType(AttackType type) { attackType = type; }

    /// <summary>
    /// Returns all living agents in the same flock (same flockId).
    /// </summary>
    public static List<GOAPBoidAgent> GetFlockmates(int id)
    {
        var mates = new List<GOAPBoidAgent>();
        for (int i = 0; i < AllAgents.Count; i++)
        {
            if (AllAgents[i].flockId == id)
                mates.Add(AllAgents[i]);
        }
        return mates;
    }

    /// <summary>
    /// Returns the centroid of all agents in the given flock.
    /// </summary>
    public static Vector3 GetFlockCentroid(int id)
    {
        Vector3 centroid = Vector3.zero;
        int count = 0;
        for (int i = 0; i < AllAgents.Count; i++)
        {
            if (AllAgents[i].flockId == id)
            {
                centroid += AllAgents[i].Position;
                count++;
            }
        }
        return count > 0 ? centroid / count : Vector3.zero;
    }

    /// <summary>
    /// Returns the number of agents in the given flock.
    /// </summary>
    public static int GetFlockCount(int id)
    {
        int count = 0;
        for (int i = 0; i < AllAgents.Count; i++)
        {
            if (AllAgents[i].flockId == id)
                count++;
        }
        return count;
    }

    private void Awake()
    {
        cachedTransform = transform;
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        currentHealth = maxHealth;
        velocity = cachedTransform.forward * maxSpeed * 0.5f;
        roomBounds = RoomBounds.Instance;
        bobPhaseOffset = Random.Range(0f, Mathf.PI * 2f);
        weavePhaseOffset = Random.Range(0f, Mathf.PI * 2f);

        Renderer rend = GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetColor("_Color", agentColor);
            rend.SetPropertyBlock(mpb);
        }
    }

    private void OnEnable()
    {
        AllAgents.Add(this);
        maxAttackersByFlock[flockId] = maxSimultaneousAttackers;
    }

    private void OnDisable() { AllAgents.Remove(this); }

    private void Update()
    {
        Profiler.BeginSample("GOAPBoidAgent.Update");

        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        // Tick per-flock swarm cooldown exactly once per frame. Whichever agent in the
        // flock runs Update first for a given frame claims the tick; all later siblings
        // see lastSwarmCooldownTickFrame[flockId] == Time.frameCount and skip. This is
        // O(1) per agent, no AllAgents scan — critical at 400+ agents.
        if (swarmCooldownByFlock.TryGetValue(flockId, out float cd) && cd > 0f)
        {
            int currentFrame = Time.frameCount;
            if (!lastSwarmCooldownTickFrame.TryGetValue(flockId, out int tickedAt) || tickedAt != currentFrame)
            {
                swarmCooldownByFlock[flockId] = cd - Time.deltaTime;
                lastSwarmCooldownTickFrame[flockId] = currentFrame;
            }
        }

        // GOAP actions set velocity via SteerToward/SetVelocity.
        // BOIDS forces layer on top — always active.

        // Obstacle avoidance — additive steering, not velocity replacement.
        // Previously this overwrote velocity, which then fought the BOIDS centroid leash
        // each frame and stalled the agent against geometry. Treat avoidance as a steering
        // force (scaled by maxSteerForce) and gate the leash when it fires this frame.
        Vector3 avoidDir = ComputeObstacleAvoidance();
        bool avoidActive = avoidDir.sqrMagnitude > 0.001f;
        if (avoidActive)
            velocity += avoidDir.normalized * maxSteerForce * 2f * Time.deltaTime;

        // BOIDS forces: separation + alignment + cohesion (always on).
        // Suppress the centroid leash this frame when avoidance fired so the leash
        // does not pull the agent back into the wall.
        Vector3 boidsForce = ComputeBoidsForces(suppressLeash: avoidActive);
        velocity += boidsForce * Time.deltaTime;

        // Boundary containment
        if (roomBounds != null)
        {
            Vector3 boundaryForce = roomBounds.GetBoundarySteeringForce(cachedTransform.position, boundaryMargin);
            velocity += boundaryForce * Time.deltaTime;
        }

        // Clamp speed
        float speed = velocity.magnitude;
        if (speed > 0.01f)
        {
            speed = Mathf.Clamp(speed, minSpeed, maxSpeed);
            velocity = velocity.normalized * speed;
        }

        // Orient toward velocity
        if (velocity.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized);
            cachedTransform.rotation = Quaternion.Slerp(
                cachedTransform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        Profiler.EndSample();
    }

    private void FixedUpdate()
    {
        float bob = Mathf.Sin((Time.fixedTime + bobPhaseOffset) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        float weave = Mathf.Sin((Time.fixedTime + weavePhaseOffset) * weaveFrequency * Mathf.PI * 2f) * weaveAmplitude;
        Vector3 right = Vector3.Cross(Vector3.up, velocity.normalized);
        if (right.sqrMagnitude < 0.001f)
            right = cachedTransform.right;

        rb.linearVelocity = velocity + Vector3.up * bob + right * weave;
    }

    public void SteerToward(Vector3 targetPosition)
    {
        Vector3 desiredDirection = (targetPosition - cachedTransform.position).normalized;
        Vector3 desiredVelocity = desiredDirection * maxSpeed;
        Vector3 steering = Vector3.ClampMagnitude(desiredVelocity - velocity, maxSteerForce);
        velocity += steering * Time.deltaTime;
    }

    public void SetVelocity(Vector3 newVelocity)
    {
        velocity = newVelocity;
    }

    /// <summary>
    /// Full BOIDS forces: separation + alignment + cohesion.
    /// This is what makes this condition different from PureGOAP (which only has separation).
    /// When <paramref name="suppressLeash"/> is true, the centroid leash is skipped — used
    /// when obstacle avoidance fired this frame to prevent the leash pulling the agent
    /// back into the wall.
    /// </summary>
    private Vector3 ComputeBoidsForces(bool suppressLeash = false)
    {
        if (AllAgents.Count <= 1)
            return Vector3.zero;

        Profiler.BeginSample("GOAPBoidAgent.BoidsForces");

        Vector3 separationForce = Vector3.zero;
        Vector3 avgVelocity = Vector3.zero;
        Vector3 centroid = Vector3.zero;
        int neighborCount = 0;

        Vector3 myPos = cachedTransform.position;
        float perceptionSq = perceptionRadius * perceptionRadius;
        float separationSq = separationRadius * separationRadius;

        for (int i = 0; i < AllAgents.Count; i++)
        {
            if (AllAgents[i] == this) continue;
            if (AllAgents[i].flockId != flockId) continue;

            Vector3 offset = AllAgents[i].Position - myPos;
            float distSq = offset.sqrMagnitude;

            if (distSq < perceptionSq)
            {
                neighborCount++;
                avgVelocity += AllAgents[i].velocity;
                centroid += AllAgents[i].Position;

                // Separation: stronger push when closer
                if (distSq < separationSq && distSq > 0.001f)
                {
                    separationForce -= offset.normalized / Mathf.Sqrt(distSq);
                }
            }
        }

        Vector3 result = Vector3.zero;

        if (neighborCount > 0)
        {
            // Separation always active
            result += separationForce.normalized * separationWeight;

            // Skip alignment + cohesion during scatter (chaotic dispersal)
            if (!isScattering)
            {
                // Alignment: steer toward average neighbor velocity
                avgVelocity /= neighborCount;
                Vector3 alignSteer = (avgVelocity - velocity).normalized;
                result += alignSteer * alignmentWeight;

                // Cohesion: steer toward neighbor centroid
                centroid /= neighborCount;
                Vector3 cohesionSteer = (centroid - myPos).normalized;
                result += cohesionSteer * cohesionWeight;
            }
        }

        // Centroid leash (manager-driven). Kicks in when this agent drifts past
        // LeashRadius from the flock's cached centroid — acts as a failsafe when
        // GOAP steers an agent farther than local cohesion can reach.
        if (flockManager != null && !isScattering && !suppressLeash)
        {
            Vector3 toCentroid = flockManager.Centroid - myPos;
            float dist = toCentroid.magnitude;
            float leash = flockManager.LeashRadius;
            if (dist > leash && leash > 0.001f)
            {
                float overshoot = (dist - leash) / leash;
                result += toCentroid.normalized * overshoot * flockManager.LeashStrength;
            }
        }

        Profiler.EndSample();
        return result;
    }

    private Vector3 ComputeObstacleAvoidance()
    {
        if (obstacleMask == 0)
            return Vector3.zero;

        Profiler.BeginSample("GOAPBoidAgent.ObstacleAvoidance");

        Vector3 moveDir = velocity.normalized;
        if (moveDir.sqrMagnitude < 0.001f)
            moveDir = cachedTransform.forward;

        if (!Physics.SphereCast(cachedTransform.position, obstacleAvoidanceRadius, moveDir,
                out RaycastHit hit, perceptionRadius, obstacleMask))
        {
            Profiler.EndSample();
            return Vector3.zero;
        }

        Vector3[] dirs = BoidHelper.Directions;
        Quaternion velRot = Quaternion.LookRotation(moveDir);
        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 worldDir = velRot * dirs[i];
            if (!Physics.SphereCast(cachedTransform.position, obstacleAvoidanceRadius, worldDir,
                    out RaycastHit _, perceptionRadius, obstacleMask))
            {
                Profiler.EndSample();
                return worldDir;
            }
        }

        Vector3 result = hit.normal;
        Profiler.EndSample();
        return result;
    }

    public void TakeDamage(float amount)
    {
        if (isDead) return;

        // When a flock manager is attached (condition 4 in-scene play), damage pools
        // into the flock and SyncBoidCountToHealth despawns tail agents. Individual
        // HP stays at max so individual-HP sensors don't mis-report.
        if (flockManager != null)
        {
            BehavioralMetricsCollector.Instance?.LogEvent(
                "Damage", gameObject.name, flockId, $"Amount={amount:F1},Pooled");
            flockManager.TakeDamage(amount);
            return;
        }

        // Fallback for tests/harnesses that spawn bare agents with no manager.
        currentHealth = Mathf.Max(currentHealth - amount, 0f);
        BehavioralMetricsCollector.Instance?.LogEvent(
            "Damage", gameObject.name, flockId, $"Amount={amount:F1},HP={currentHealth:F1}");

        if (currentHealth <= 0f)
        {
            isDead = true;
            BehavioralMetricsCollector.Instance?.LogEvent(
                "Death", gameObject.name, flockId, "Destroyed");
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        // Manager may have destroyed this via SyncBoidCountToHealth; it may also
        // already be gone during ClearAgents teardown. Unity's == overload treats
        // fake-null (destroyed) refs as null, so this guards both cases.
        if (flockManager != null)
            flockManager.UnregisterAgent(this);
    }

    private void OnDrawGizmos()
    {
        if (!drawDebug || cachedTransform == null) return;

        Gizmos.color = Color.green;
        Gizmos.DrawLine(cachedTransform.position, cachedTransform.position + velocity);

        Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
        Gizmos.DrawWireSphere(cachedTransform.position, perceptionRadius);

        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(cachedTransform.position, separationRadius);

        if (targetPlayer != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(cachedTransform.position, targetPlayer.position);
            Gizmos.DrawWireSphere(cachedTransform.position, attackRange);
        }
    }
}
