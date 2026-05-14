using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Pure GOAP agent for Condition 1: Individual GOAP planning with direct steering.
/// No flocking behaviors - agents act independently using only GOAP for decision-making.
///
/// Movement: Direct steering toward GOAP action targets with 3D swimming/bobbing
/// Planning: Individual GOAP brain per agent
/// Obstacle Avoidance: SphereCast-based (same as BOIDS system)
/// Swarm: Separation force to prevent overlap (cohesion lives in PureSwarmMoveAction)
/// Bounds: Stays within RoomBounds if present in scene
/// Target: Player (detected via VisionSensor)
///
/// Components required:
/// - This script
/// - GoapActionProvider (CrashKonijn)
/// - AgentBehaviour (CrashKonijn)
/// - PureGOAPBrain (goal selection)
/// </summary>
public class PureGOAPAgent : MonoBehaviour, IEnemy
{
    // Static registry of all living agents (for swarm forces and sensors)
    public static readonly List<PureGOAPAgent> AllAgents = new List<PureGOAPAgent>();

    // Swarm-wide attack cooldown — only one attack per swarm pass
    public static float SwarmAttackCooldown;
    [Header("Swarm Attack")]
    [SerializeField] private float swarmCooldownDuration = 3f;
    public float SwarmCooldownDuration => swarmCooldownDuration;

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

    [Header("Swarm Separation")]
    [SerializeField] private float separationRadius = 4f;
    [SerializeField] private float separationWeight = 3f;

    [Header("Room Bounds")]
    [SerializeField] private float boundaryMargin = 10f;

    [Header("Swimming/Bobbing")]
    [SerializeField] private float bobAmplitude = 1.5f;
    [SerializeField] private float bobFrequency = 1.2f;
    [SerializeField] private float weaveAmplitude = 1.2f;
    [SerializeField] private float weaveFrequency = 0.8f;

    [Header("Appearance")]
    [SerializeField] private Color agentColor = Color.green;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = false;

    // Public state (accessed by GOAP actions)
    [HideInInspector] public Vector3 velocity;
    [HideInInspector] public Transform cachedTransform;
    [HideInInspector] public float cooldownTimer;
    [HideInInspector] public Transform targetPlayer;

    private float currentHealth;
    private bool isDead;
    private RoomBounds roomBounds;
    private float bobPhaseOffset;
    private float weavePhaseOffset;
    private Rigidbody rb;

    // IEnemy interface
    public bool IsDead => isDead;
    public float HealthPercent => currentHealth / maxHealth;
    public Vector3 Position => cachedTransform.position;
    public Vector3 Velocity => velocity;

    public float MaxSpeed => maxSpeed;
    public float AttackRange => attackRange;
    public float AttackDamage => attackDamage;
    public float AttackCooldown => attackCooldown;
    public AttackType AgentAttackType => attackType;
    public GameObject ProjectilePrefab => projectilePrefab;
    public void SetAttackType(AttackType type) { attackType = type; }

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

        // Set agent color
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
    }

    private void OnDisable()
    {
        AllAgents.Remove(this);
    }

    private void Update()
    {
        Profiler.BeginSample("PureGOAPAgent.Update");

        // Tick cooldowns
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;
        if (SwarmAttackCooldown > 0f)
            SwarmAttackCooldown -= Time.deltaTime;

        // GOAP actions set velocity via SteerToward/SetVelocity.
        // Here we layer on environmental forces (always active).

        // Obstacle avoidance
        Vector3 avoidDir = ComputeObstacleAvoidance();
        if (avoidDir.sqrMagnitude > 0.001f)
        {
            float currentSpeed = velocity.magnitude;
            velocity = avoidDir.normalized * currentSpeed;
        }

        // Separation (always on — collision avoidance between agents)
        Vector3 swarmForce = ComputeSwarmForces();
        velocity += swarmForce * Time.deltaTime;

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
        // Vertical bob (up/down sine wave)
        float bob = Mathf.Sin((Time.fixedTime + bobPhaseOffset) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;

        // Horizontal weave (side-to-side perpendicular to movement direction)
        float weave = Mathf.Sin((Time.fixedTime + weavePhaseOffset) * weaveFrequency * Mathf.PI * 2f) * weaveAmplitude;
        Vector3 right = Vector3.Cross(Vector3.up, velocity.normalized);
        if (right.sqrMagnitude < 0.001f)
            right = cachedTransform.right;

        // Set Rigidbody velocity — physics handles collisions, interpolation handles smoothness
        rb.linearVelocity = velocity + Vector3.up * bob + right * weave;
    }

    /// <summary>
    /// Steer toward a target using direct steering (no flocking).
    /// Called by GOAP actions to set movement direction.
    /// </summary>
    public void SteerToward(Vector3 targetPosition)
    {
        Vector3 desiredDirection = (targetPosition - cachedTransform.position).normalized;
        Vector3 desiredVelocity = desiredDirection * maxSpeed;
        Vector3 steering = Vector3.ClampMagnitude(desiredVelocity - velocity, maxSteerForce);
        velocity += steering * Time.deltaTime;
    }

    /// <summary>
    /// Set velocity directly (used by GOAP actions for precise movement control).
    /// </summary>
    public void SetVelocity(Vector3 newVelocity)
    {
        velocity = newVelocity;
    }

    /// <summary>
    /// Separation force to prevent agents from overlapping.
    /// Cohesion is handled by PureSwarmMoveAction — this is just collision avoidance.
    /// </summary>
    private Vector3 ComputeSwarmForces()
    {
        if (AllAgents.Count <= 1)
            return Vector3.zero;

        Profiler.BeginSample("PureGOAPAgent.SwarmForces");

        Vector3 separationForce = Vector3.zero;
        Vector3 myPos = cachedTransform.position;
        float separationRadiusSq = separationRadius * separationRadius;

        for (int i = 0; i < AllAgents.Count; i++)
        {
            if (AllAgents[i] == this) continue;

            Vector3 offset = AllAgents[i].Position - myPos;
            float distSq = offset.sqrMagnitude;

            if (distSq <= separationRadiusSq && distSq > 0.001f)
            {
                separationForce -= offset.normalized / Mathf.Sqrt(distSq);
            }
        }

        Vector3 result = separationForce.normalized * separationWeight;

        Profiler.EndSample();
        return result;
    }

    /// <summary>
    /// Obstacle avoidance using SphereCast with golden ratio direction sampling.
    /// Same system as BOIDS for consistency.
    /// </summary>
    private Vector3 ComputeObstacleAvoidance()
    {
        if (obstacleMask == 0)
            return Vector3.zero;

        Profiler.BeginSample("PureGOAPAgent.ObstacleAvoidance");

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

    // IEnemy interface implementation
    public void TakeDamage(float amount)
    {
        if (isDead) return;

        currentHealth = Mathf.Max(currentHealth - amount, 0f);
        if (currentHealth <= 0f)
        {
            isDead = true;
            Destroy(gameObject);
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawDebug || cachedTransform == null) return;

        // Draw velocity vector
        Gizmos.color = Color.green;
        Gizmos.DrawLine(cachedTransform.position, cachedTransform.position + velocity);

        // Draw separation radius
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(cachedTransform.position, separationRadius);

        // Draw attack range
        if (targetPlayer != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(cachedTransform.position, targetPlayer.position);
            Gizmos.DrawWireSphere(cachedTransform.position, attackRange);
        }
    }
}
