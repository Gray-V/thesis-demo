using System.Collections.Generic;
using UnityEngine;

public class BoidAgent : MonoBehaviour, IEnemy
{
    // Arena floor is at y=0 (the BoxCollider named "r" in New Scene.unity, scale
    // 20x1x20). Boids may not descend below y = kFloorY + kBoidGroundOffset; the
    // offset accounts for the boid mesh half-height. See ApplyFloorClamp.
    public const float kFloorY = 0f;
    public const float kBoidGroundOffset = 0.5f;

    // Static buffers for the obstacle-avoidance physics queries in
    // ComputeObstacleAvoidance — no per-frame GC. The boid prefab carries a
    // collider on the same "Environment" layer that obstacleMask targets, so
    // every query also picks up fellow boids; the buffers must be large enough
    // to hold several boids AND still surface the real geometry behind them
    // (IsBoidCollider filters the boids out in code).
    private static readonly Collider[] sOverlapBuffer = new Collider[16];
    private static readonly RaycastHit[] sCastBuffer = new RaycastHit[16];

    // Every live boid's own collider, for O(1) "is this hit a fellow boid?"
    // checks in ComputeObstacleAvoidance. Populated in Awake, removed in
    // OnDestroy. A HashSet lookup is far cheaper than a GetComponentInParent
    // run per hit, per cast, per boid, per frame — that per-hit component walk
    // was the source of the near-geometry lag spikes.
    private static readonly HashSet<Collider> sBoidColliders = new HashSet<Collider>();
    private Collider ownCollider;

    [HideInInspector] public BoidSettings settings;
    [HideInInspector] public FlockManager manager;

    // Set by BoidGoapBrain.Awake(); null when GOAP package is not installed or component is absent.
    [HideInInspector] public BoidGoapBrain goapBrain;

    // Subgroup bucket for Scatter (and any other behavior that wants stable
    // subdivisions). Assigned at spawn time by FlockManager.SpawnFlock; the
    // total bucket count is exposed by FlockManager.ScatterSubgroupCount.
    [HideInInspector] public int subgroupId;

    private enum AttackState { Flocking, WindUp, InfinitySweep, Formation }

    // Public so GOAP actions can set velocity directly when they own movement.
    [HideInInspector] public Vector3 velocity;
    [HideInInspector] public Transform cachedTransform;
    private AttackState attackState = AttackState.Flocking;
    private float attackStateTimer;
    private float cooldownTimer;
    // Infinity sweep state
    private Vector3 infinityTipA;
    private Vector3 infinityCenter;
    private Vector3 infinityForward;
    private Vector3 infinityRight;
    private float   infinityR;
    private float   infinitySweepTimer;
    private bool inFormation;
    private Vector3 formationTargetPos;

    // Cached Scatter direction — picked once per entry into Scattering so the
    // intra-subgroup jitter doesn't re-roll every frame (which would produce
    // visible per-frame zig-zag).
    private Vector3 cachedScatterDir;
    private bool wasScatteringLastFrame;

    // IEnemy proxies to the flock — combat teammate calls these without knowing about flocks
    public bool IsDead => manager.IsDead;
    public float HealthPercent => manager.HealthPercent;

    public void TakeDamage(float amount)
    {
        BehavioralMetricsCollector.Instance?.LogEvent(
            "Damage", gameObject.name, -1, $"Amount={amount:F1}");
        manager.TakeDamage(amount);
    }

    public Vector3 Position => cachedTransform.position;
    public Vector3 Velocity => velocity;
    public FlockType FlockType => settings.flockType;

    private void Awake()
    {
        cachedTransform = transform;
        ownCollider = GetComponent<Collider>();
        if (ownCollider != null)
            sBoidColliders.Add(ownCollider);
    }

    private void OnDestroy()
    {
        if (ownCollider != null)
            sBoidColliders.Remove(ownCollider);
    }

    public void Initialize(Vector3 startVelocity)
    {
        velocity = startVelocity;
    }

    public void UpdateBoid(Vector3 separationHeading, Vector3 alignmentHeading, Vector3 cohesionCenter, int neighborCount, Vector3 crossFlockSeparation, bool debugLog = false)
    {
        Vector3 acceleration = Vector3.zero;

        // Obstacle-avoidance steering — populated below and applied to velocity
        // even during a GOAP movement override, so the leader cannot clip through
        // walls mid-attack.
        Vector3 obstacleAccel = Vector3.zero;

        // Movement diagnostics — only populated when this boid is the sampled one
        // (FlockManager passes debugLog=true for boids[0] on debug-tick frames).
        Vector3 debugStartPos = debugLog ? cachedTransform.position : default;
        Vector3 debugStartVel = debugLog ? velocity : default;

        // Suppress flocking and seek forces only during full movement override (ranged formation)
        if (!IsMovementOverridden)
        {
            // State-based modulation of the three core flocking rules.
            // Scattering: kill alignment+cohesion so boids disperse chaotically.
            // Regrouping: boost cohesion so the flock rapidly re-tightens.
            FlockManager.FlockState s = manager.State;
            bool skipAlignmentCohesion = s == FlockManager.FlockState.Scattering;
            float cohesionMul = s == FlockManager.FlockState.Regrouping
                ? settings.regroupCohesionMultiplier
                : 1f;

            if (neighborCount > 0)
            {
                acceleration += SteerTowards(separationHeading) * settings.separationWeight;
                if (!skipAlignmentCohesion)
                {
                    acceleration += SteerTowards(alignmentHeading) * settings.alignmentWeight;
                    acceleration += SteerTowards(cohesionCenter) * settings.cohesionWeight * cohesionMul;
                }
            }
            else if (!skipAlignmentCohesion
                     && manager.LeaderBoid != null
                     && this != manager.LeaderBoid
                     && manager.LeaderBoid.gameObject != null)
            {
                // Stranded follower (zero in-perception neighbors). FlockManager has
                // pre-filled cohesionCenter with the leader-redirect vector; apply it
                // so the follower can recover instead of drifting away forever.
                acceleration += SteerTowards(cohesionCenter) * settings.cohesionWeight * cohesionMul;
            }

            // Cross-flock avoidance (independent of same-flock neighbors)
            acceleration += SteerTowards(crossFlockSeparation) * settings.crossFlockSeparationWeight;

            // Boundary steering
            acceleration += ComputeBoundarySteer();

            // Scattering: each boid gets a subgroup-bucketed outward push from the flock
            // centroid. Three subgroups → flock visibly splits into three dispersal lobes
            // instead of every agent fanning out independently. Direction is cached on
            // entry into Scattering to avoid per-frame jitter from the bucket randomness.
            bool isScattering = s == FlockManager.FlockState.Scattering;
            if (isScattering)
            {
                if (!wasScatteringLastFrame)
                {
                    cachedScatterDir = ScatterDirectionPicker.PickScatterDirection(
                        cachedTransform.position, manager.GetFlockCenter(),
                        subgroupId, FlockManager.ScatterSubgroupCount);
                }
                acceleration += cachedScatterDir * settings.maxSteerForce;
            }
            wasScatteringLastFrame = isScattering;

            // Target-seek — per-state behavior.
            if (manager.Target != null && settings.targetSeekWeight > 0f)
            {
                Vector3 targetOffset = manager.Target.position - cachedTransform.position;
                float targetDist = targetOffset.magnitude;

                switch (s)
                {
                    case FlockManager.FlockState.Engaging:
                        if (targetDist > settings.targetKeepDistance)
                            acceleration += SteerTowards(targetOffset) * settings.targetSeekWeight;
                        else
                            acceleration += SteerTowards(-targetOffset) * settings.targetSeekWeight;
                        break;

                    case FlockManager.FlockState.Fleeing:
                    case FlockManager.FlockState.Kiting:
                    case FlockManager.FlockState.Scattering:
                        // Steer AWAY from the target. Kiting still lets the flock's
                        // ranged machine fire volleys from the backpedaling position.
                        acceleration += SteerTowards(-targetOffset) * settings.targetSeekWeight;
                        break;

                    case FlockManager.FlockState.Guarding:
                        // Hold the perimeter at guardInnerRange — step in if beyond, back off if inside.
                        if (targetDist > settings.guardInnerRange + 1f)
                            acceleration += SteerTowards(targetOffset) * settings.targetSeekWeight * 0.5f;
                        else if (targetDist < settings.guardInnerRange - 1f)
                            acceleration += SteerTowards(-targetOffset) * settings.targetSeekWeight * 0.5f;
                        break;

                    case FlockManager.FlockState.Flanking:
                    {
                        // Rotate target-seek by ±flankAngleDegrees around Y — side of
                        // the angle picked from this boid's current side of the target
                        // so the flock splits into two wings instead of one lane.
                        float sign = cachedTransform.position.x >= manager.Target.position.x ? 1f : -1f;
                        Quaternion rot = Quaternion.AngleAxis(sign * settings.flankAngleDegrees, Vector3.up);
                        Vector3 flankedDir = rot * targetOffset;
                        if (targetDist > settings.targetKeepDistance)
                            acceleration += SteerTowards(flankedDir) * settings.targetSeekWeight;
                        else
                            acceleration += SteerTowards(-flankedDir) * settings.targetSeekWeight;
                        break;
                    }

                    // Idle / Grouping / Regrouping: no per-boid target-seek — manager
                    // anchor movement handles overall positioning; cohesion keeps the flock tight.
                }
            }
        }

        // Attack state machine (legacy FSM) — only runs when the active condition does NOT use GOAP for boids.
        // When GOAP is active (BOIDSWithGOAPLeader), GOAP actions drive velocity directly via BoidGoapBrain.
        bool useGoap = ConditionManager.Instance != null && ConditionManager.Instance.UsesGoapForBoids;
        if (!useGoap
            && manager.Target != null
            && manager.State == FlockManager.FlockState.Engaging
            && !manager.IsDead)
        {
            if (settings.flockType == FlockType.Melee)
                UpdateMeleeAttackState(ref acceleration);
            else if (settings.flockType == FlockType.Ranged)
                UpdateRangedFormationState();
        }

        // Obstacle avoidance
        obstacleAccel = ComputeObstacleAvoidance();
        acceleration += obstacleAccel;

        // Apply acceleration to velocity (skip during full movement override)
        if (!IsMovementOverridden)
        {
            velocity += acceleration * Time.deltaTime;

            float speed = velocity.magnitude;
            if (speed > 0f)
            {
                // Fleeing / Kiting get a modest speed boost, Scattering a larger one
                // so the flock visibly panics vs merely retreating.
                float maxSpd = settings.maxSpeed;
                switch (manager.State)
                {
                    case FlockManager.FlockState.Fleeing:
                    case FlockManager.FlockState.Kiting:
                        maxSpd *= settings.fleeSpeedMultiplier;
                        break;
                    case FlockManager.FlockState.Scattering:
                        maxSpd *= settings.scatterSpeedMultiplier;
                        break;
                }
                speed = Mathf.Clamp(speed, settings.minSpeed, maxSpd);
                velocity = velocity.normalized * speed;
            }
        }
        else
        {
            // A GOAP action owns the primary trajectory (e.g. the leader's Attack/
            // Flank/Kite), but obstacle avoidance still applies on top so it cannot
            // clip straight through walls. The overlap-recovery term inside
            // obstacleAccel also shoves it back out if it does penetrate geometry.
            velocity += obstacleAccel * Time.deltaTime;
        }

        // Move and orient
        cachedTransform.position += velocity * Time.deltaTime;

        // Floor containment — last-resort guard against tunnelling and -Y drift.
        // Mirrors the XZ boundary radius; this is the Y analogue. Cheap (one
        // comparison per boid per frame). No-op when the boid is already above
        // floor, which is the common case.
        Vector3 pos = cachedTransform.position;
        if (ApplyFloorClamp(ref pos, ref velocity, kFloorY + kBoidGroundOffset))
            cachedTransform.position = pos;

        if (velocity.sqrMagnitude > 0.001f)
        {
            cachedTransform.forward = velocity.normalized;
        }

        if (debugLog && FlockManager.MovementDebug)
        {
            Vector3 endPos = cachedTransform.position;
            bool nan = float.IsNaN(velocity.x) || float.IsNaN(velocity.y) || float.IsNaN(velocity.z)
                       || float.IsNaN(endPos.x) || float.IsNaN(endPos.y) || float.IsNaN(endPos.z);
            Debug.Log(
                $"[BoidDebug] {name} nbrs={neighborCount} state={manager.State} override={IsMovementOverridden}" +
                $" | in: sep={separationHeading.magnitude:F2} align={alignmentHeading.magnitude:F2} coh={cohesionCenter.magnitude:F2} xflock={crossFlockSeparation.magnitude:F2}" +
                $" | obstacleAccel={obstacleAccel.magnitude:F2} totalAccel={acceleration.magnitude:F2}" +
                $" | spd {debugStartVel.magnitude:F2}->{velocity.magnitude:F2} (min={settings.minSpeed} max={settings.maxSpeed})" +
                $" | moved={(endPos - debugStartPos).magnitude:F3} pos={endPos}" +
                $" | dt={Time.deltaTime:F4} timeScale={Time.timeScale:F2}" +
                (nan ? "  <<<<< NaN DETECTED" : ""),
                this);
        }
    }

    /// <summary>
    /// Hard Y-clamp: if <paramref name="pos"/>.y is below <paramref name="minY"/>,
    /// raises it to minY and zeroes any downward component of <paramref name="vel"/>.
    /// Returns true when the clamp actually fired (used by the caller to skip the
    /// position write in the common above-floor case). Pure function modulo the
    /// ref params — extracted out of UpdateBoid so EditMode tests can hit it
    /// without the manager / settings / scene-graph scaffolding.
    /// </summary>
    public static bool ApplyFloorClamp(ref Vector3 pos, ref Vector3 vel, float minY)
    {
        if (pos.y >= minY) return false;
        pos.y = minY;
        if (vel.y < 0f) vel.y = 0f;
        return true;
    }

    private bool ConditionUsesGoap =>
        ConditionManager.Instance != null && ConditionManager.Instance.UsesGoapForBoids;

    public bool IsAttacking =>
        (ConditionUsesGoap && goapBrain != null && goapBrain.isGoapAttacking)
        || (!ConditionUsesGoap && attackState != AttackState.Flocking);

    // True for states that fully override movement (both legacy FSM and GOAP paths).
    private bool IsMovementOverridden =>
        attackState == AttackState.Formation
        || attackState == AttackState.InfinitySweep
        || attackState == AttackState.WindUp
        || (ConditionUsesGoap && goapBrain != null && goapBrain.isMovementOverridden);

    public void SetFormationTarget(Vector3 worldPos, bool active)
    {
        formationTargetPos = worldPos;
        inFormation = active;
        if (active)
            attackState = AttackState.Formation;
        else if (attackState == AttackState.Formation)
            attackState = AttackState.Flocking;
    }

    public void BeginMeleeWindUp()
    {
        attackState = AttackState.WindUp;
    }

    public void BeginInfinitySweep()
    {
        SetupInfinityPath();
        infinitySweepTimer = 0f;
        attackState = AttackState.InfinitySweep;
    }

    public void EndMeleeAttack()
    {
        attackState = AttackState.Flocking;
    }

    private void UpdateMeleeAttackState(ref Vector3 acceleration)
    {
        switch (attackState)
        {
            case AttackState.WindUp:
                velocity = Vector3.zero;
                break;

            case AttackState.InfinitySweep:
            {
                float t = Mathf.Clamp01(infinitySweepTimer / settings.attackSweepDuration);
                Vector3 targetPos = EvaluateInfinityPath(t);
                velocity = (targetPos - cachedTransform.position) / Time.deltaTime;
                infinitySweepTimer += Time.deltaTime;
                break;
            }
        }
    }

    private void UpdateRangedFormationState()
    {
        if (!inFormation) return;

        // Fly toward assigned formation slot
        Vector3 toSlot = formationTargetPos - cachedTransform.position;
        float dist = toSlot.magnitude;

        if (dist > 0.3f)
        {
            velocity = toSlot.normalized * settings.formationApproachSpeed;
        }
        else
        {
            // Hold position — hover with minimal drift
            velocity = toSlot * 2f;
        }

        // Face toward the player while in formation
        if (manager.Target != null)
        {
            Vector3 toTarget = manager.Target.position - cachedTransform.position;
            if (toTarget.sqrMagnitude > 0.01f)
                cachedTransform.forward = toTarget.normalized;
        }
    }

    public void ApplyKnockback(Vector3 impulse)
    {
        // Clamp downward component so player attacks cannot punt boids through
        // the floor. Lateral and upward knockback are unchanged.
        if (impulse.y < 0f) impulse.y = 0f;
        velocity += impulse;
    }

    private void SetupInfinityPath()
    {
        infinityTipA   = cachedTransform.position;
        infinityCenter = manager.Target.position;

        Vector3 toCenter = infinityCenter - infinityTipA;
        float D = toCenter.magnitude;
        if (D < 0.01f) { infinityForward = cachedTransform.forward; D = 2f; }
        else             infinityForward = toCenter.normalized;

        infinityRight = Mathf.Abs(Vector3.Dot(infinityForward, Vector3.up)) > 0.99f
            ? Vector3.Cross(infinityForward, Vector3.right).normalized
            : Vector3.Cross(Vector3.up, infinityForward).normalized;

        infinityR = D * 0.5f;
    }

    private Vector3 EvaluateInfinityPath(float t)
    {
        if (t <= 0.5f)
        {
            float angle = t * 2f * Mathf.PI;
            Vector3 c1 = infinityTipA + infinityForward * infinityR;
            return c1 + infinityR * (-infinityForward * Mathf.Cos(angle)
                                     + infinityRight   * Mathf.Sin(angle));
        }
        else
        {
            float angle = (t - 0.5f) * 2f * Mathf.PI;
            Vector3 c2 = infinityCenter + infinityForward * infinityR;
            return c2 + infinityR * (-infinityForward * Mathf.Cos(angle)
                                     - infinityRight   * Mathf.Sin(angle));
        }
    }

    private Vector3 SteerTowards(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
            return Vector3.zero;

        Vector3 steer = direction.normalized * settings.maxSpeed - velocity;
        return Vector3.ClampMagnitude(steer, settings.maxSteerForce);
    }

    private Vector3 ComputeBoundarySteer()
    {
        Vector3 managerPos = manager.AnchorPosition;
        Vector3 offset = cachedTransform.position - managerPos;
        float distance = offset.magnitude;
        float boundaryRadius = manager.EffectiveBoundaryRadius;

        if (distance < boundaryRadius)
            return Vector3.zero;

        // Steer back toward center, strength proportional to how far past the boundary
        float overshoot = distance - boundaryRadius;
        Vector3 directionToCenter = -offset.normalized;
        float strength = overshoot * settings.boundaryTurnStrength;
        float maxBoundaryForce = settings.obstacleAvoidanceWeight * settings.maxSteerForce;
        strength = Mathf.Min(strength, maxBoundaryForce);
        return directionToCenter * strength;
    }

    private Vector3 ComputeObstacleAvoidance()
    {
        if (settings.obstacleMask == 0)
            return Vector3.zero;

        // Recovery probe: SphereCast cannot detect colliders the cast origin is
        // already inside (Unity engine limitation), so once a boid penetrates an
        // obstacle the forward avoidance below stops working for that boid /
        // obstacle pair. OverlapSphereNonAlloc is the only API that catches the
        // already-inside case. Static buffer = no per-frame GC; tiny radius +
        // single-layer mask = microsecond-cheap in the common (empty) case.
        int overlapCount = Physics.OverlapSphereNonAlloc(
            cachedTransform.position,
            settings.obstacleAvoidanceRadius * 0.5f,
            sOverlapBuffer,
            settings.obstacleMask);
        Collider overlapObstacle = null;
        for (int o = 0; o < overlapCount; o++)
        {
            // Skip fellow boids — only real geometry counts as an obstacle.
            if (!IsBoidCollider(sOverlapBuffer[o]))
            {
                overlapObstacle = sOverlapBuffer[o];
                break;
            }
        }
        if (overlapObstacle != null)
        {
            Vector3 closest = GetEscapeReference(overlapObstacle, cachedTransform.position);
            Vector3 outward = cachedTransform.position - closest;
            // Degenerate (cast origin coincides with closest point — center of a
            // box, etc.): push straight up so the boid escapes onto the obstacle
            // surface rather than sticking forever.
            if (outward.sqrMagnitude < 0.0001f)
                outward = Vector3.up;
            return ComputeOverlapEscape(outward, settings.maxSteerForce, settings.obstacleAvoidanceWeight);
        }

        Vector3 forward = cachedTransform.forward;

        // Check if there's real geometry ahead
        if (!ObstacleAhead(forward, out RaycastHit hit))
            return Vector3.zero;

        // Find the first unobstructed direction
        Vector3[] dirs = BoidHelper.Directions;
        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 worldDir = cachedTransform.TransformDirection(dirs[i]);
            if (!ObstacleAhead(worldDir, out RaycastHit _))
                return SteerTowards(worldDir) * settings.obstacleAvoidanceWeight;
        }

        // All directions blocked — steer away from the hit
        return SteerTowards(-hit.normal) * settings.obstacleAvoidanceWeight;
    }

    /// <summary>
    /// True when <paramref name="col"/> belongs to a boid rather than real
    /// environment geometry (or is null). The boid prefab sits on the same
    /// layer obstacleMask targets, so the physics queries in
    /// ComputeObstacleAvoidance also catch fellow boids — but a boid is never an
    /// obstacle. Same-flock spacing is the separation rule's job; treating boids
    /// as obstacles makes every agent shove every neighbour at full obstacle
    /// force (and, when a boid catches its own collider, launches it straight up
    /// via the degenerate-escape fallback).
    /// </summary>
    private static bool IsBoidCollider(Collider col)
    {
        // O(1) set lookup — see sBoidColliders. Null counts as "skip" so a
        // stale/destroyed hit is never treated as an obstacle.
        return col == null || sBoidColliders.Contains(col);
    }

    /// <summary>
    /// SphereCast along <paramref name="dir"/> that ignores fellow boids and
    /// reports the nearest real-geometry hit. Returns false when the path is
    /// clear of actual obstacles. Uses SphereCastNonAlloc + a static buffer so
    /// there is no per-frame GC; like all SphereCasts it cannot detect colliders
    /// the origin is already inside — that case is covered by the OverlapSphere
    /// recovery probe above.
    /// </summary>
    private bool ObstacleAhead(Vector3 dir, out RaycastHit obstacleHit)
    {
        obstacleHit = default;
        int n = Physics.SphereCastNonAlloc(
            cachedTransform.position, settings.obstacleAvoidanceRadius, dir,
            sCastBuffer, settings.perceptionRadius, settings.obstacleMask);

        float nearest = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (IsBoidCollider(sCastBuffer[i].collider))
                continue;
            if (sCastBuffer[i].distance < nearest)
            {
                nearest = sCastBuffer[i].distance;
                obstacleHit = sCastBuffer[i];
                found = true;
            }
        }
        return found;
    }

    /// <summary>
    /// Reference point the inside-collider recovery pushes away from.
    /// Collider.ClosestPoint only supports Box/Sphere/Capsule/convex-Mesh
    /// colliders — it THROWS for non-convex MeshColliders and TerrainColliders,
    /// which is exactly what the scene's Environment geometry uses. For those,
    /// fall back to the collider's AABB center: less precise than the true
    /// surface point, but always valid and still a sensible "push outward"
    /// anchor for escaping a penetrated obstacle. Public for EditMode tests.
    /// </summary>
    public static Vector3 GetEscapeReference(Collider collider, Vector3 position)
    {
        bool supportsClosestPoint =
            !(collider is TerrainCollider)
            && !(collider is MeshCollider mesh && !mesh.convex);

        return supportsClosestPoint
            ? collider.ClosestPoint(position)
            : collider.bounds.center;
    }

    /// <summary>
    /// Math half of the inside-collider recovery: turn an outward vector into a
    /// steering acceleration scaled by <paramref name="weight"/> and capped at
    /// <paramref name="maxSteerForce"/>. Pure function — extracted so EditMode
    /// tests can verify the magnitude / direction without invoking Physics.
    /// </summary>
    public static Vector3 ComputeOverlapEscape(Vector3 outward, float maxSteerForce, float weight)
    {
        if (outward.sqrMagnitude < 0.0001f)
            return Vector3.zero;
        return outward.normalized * maxSteerForce * weight;
    }
}
