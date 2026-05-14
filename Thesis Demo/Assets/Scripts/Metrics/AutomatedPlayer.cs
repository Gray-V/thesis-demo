using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;
using Object = UnityEngine.Object;

/// <summary>
/// Automated player controller simulating a hack-and-slash playstyle for thesis experiments.
///
/// Phases (weighted random, selected at each transition — all Random calls happen here, not per-frame):
///   Wander        — patrol randomly
///   Approach      — move toward enemy group
///   CircleStrafe  — orbit enemy group at medium range (bread-and-butter hack-and-slash positioning)
///   HitAndRun     — close in, attack burst, dodge back out
///   StandAndFight — plant and spam attacks (tests sustained pressure)
///   Kite          — maintain distance, use ranged attacks
///   Retreat       — flee when health is low
///
/// Combat (all logged to BehavioralMetricsCollector):
///   LightAttack   — fast melee cone, chains into combo counter
///   HeavyAttack   — slow wind-up AOE finisher triggered after maxComboCount light hits
///   AOEBurst      — 360° spin/slam hitting all nearby enemies, long cooldown
///   RangedThrow   — single-target ranged, used while kiting or when nothing is in melee range
///   DodgeRoll     — speed burst + i-frames, cancels on next attack
///
/// Seed note: all Random.* calls are in phase-transition methods only (PickNewPhase/Enter*).
/// ConditionManager.InitState(seed) before spawn makes the full run deterministic.
/// </summary>
public class AutomatedPlayer : MonoBehaviour
{
    public enum PlayerPhase { Wander, Approach, CircleStrafe, HitAndRun, StandAndFight, Kite, Retreat }

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Phase Weights (should sum to ~1; Kite gets the remainder)")]
    [SerializeField] private float wanderWeight       = 0.10f;
    [SerializeField] private float approachWeight     = 0.25f;
    [SerializeField] private float circleStrafeWeight = 0.25f;
    [SerializeField] private float hitAndRunWeight    = 0.20f;
    [SerializeField] private float standAndFightWeight= 0.12f;
    // Kite is the fallback — it takes whatever probability remains after the weights above.

    [Header("Orbit (CircleStrafe / Kite)")]
    [SerializeField] private float orbitRadius = 9f;
    [SerializeField] private float orbitSpeed  = 55f;  // degrees per second
    [SerializeField] private float kiteRange   = 16f;

    [Header("Retreat")]
    [SerializeField] private float retreatHealthThreshold = 0.35f;
    [SerializeField] private float retreatDuration        = 4f;

    [Header("Light Attack")]
    [SerializeField] private float lightAttackRange    = 7f;
    [SerializeField] private float lightAttackDamage   = 7f;
    [SerializeField] private float lightAttackCooldown = 0.55f;
    [SerializeField] private float lightAttackConeHalfAngle = 65f;
    // Cap on enemies hit by one swing. Without this, dense clusters absorb the
    // full melee cone and a single condition's tight-cohesion behavior drains
    // its pooled HP super-linearly with N — see 2026-05-04 floor diagnostic.
    [SerializeField] private int   lightAttackMaxTargets = 3;

    [Header("Combo / Heavy Attack")]
    [SerializeField] private int   maxComboCount       = 3;
    [SerializeField] private float comboWindowDuration = 1.3f;
    [SerializeField] private float heavyAttackRange    = 6f;
    [SerializeField] private float heavyAttackDamage   = 22f;
    [SerializeField] private float heavyAttackCooldown = 2.5f;
    [SerializeField] private float heavyWindUpDuration = 0.5f;
    [SerializeField] private int   heavyAttackMaxTargets = 6;

    [Header("AOE Burst (spin attack)")]
    [SerializeField] private float aoeBurstRange    = 5f;
    [SerializeField] private float aoeBurstDamage   = 9f;
    [SerializeField] private float aoeBurstCooldown = 7f;
    [SerializeField] private int   aoeBurstMaxTargets = 8;

    [Header("Ranged Throw")]
    [SerializeField] private float rangedThrowRange    = 28f;
    [SerializeField] private float rangedThrowDamage   = 10f;
    [SerializeField] private float rangedThrowCooldown = 2.8f;

    [Header("Dodge Roll")]
    [SerializeField] private float dodgeSpeedMultiplier = 3.2f;
    [SerializeField] private float dodgeDuration        = 0.35f;
    [SerializeField] private float dodgeCooldown        = 1.0f;

    [Header("Enemy Detection")]
    [Tooltip("Layer mask for enemies. If 0, falls back to static agent lists (works without layer setup).")]
    [SerializeField] private LayerMask enemyLayerMask;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = false;

    // ── Runtime state ──
    private PlayerPhase currentPhase;
    private Vector3     moveDirection;
    private float       phaseTimer;
    private float       orbitAngle;

    private float lightAttackTimer;
    private float heavyAttackTimer;
    private float aoeBurstTimer;
    private float rangedThrowTimer;
    private float dodgeCooldownTimer;

    private int   comboCount;
    private float comboWindowTimer;
    private bool  heavyWindingUp;
    private float heavyWindTimer;

    private bool    isDodging;
    private float   dodgeTimer;
    private Vector3 dodgeDirection;

    private PlayerHealth playerHealth;
    private RoomBounds   roomBounds;
    private NavMeshAgent navAgent;     // Optional — when present, movement routes through NavMesh.Move() so the player respects arena walls/obstacles instead of clipping through them. Null in test scenes without a baked NavMesh, in which case we fall back to direct transform translation.

    // Cached scene lookups — refreshed every 0.5s to avoid per-frame FindObjectsByType calls
    private BoidAgent[]    cachedBoids    = new BoidAgent[0];
    private FlockManager[] cachedManagers = new FlockManager[0];
    private float          cacheRefreshTimer;

    // ── Public state (readable by tests) ──
    public PlayerPhase CurrentPhase     => currentPhase;
    public int         ComboCount       => comboCount;
    public bool        IsDodging        => isDodging;
    public bool        IsHeavyWindingUp => heavyWindingUp;

    // ─────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        playerHealth = GetComponent<PlayerHealth>();
        roomBounds   = RoomBounds.Instance;
        navAgent     = GetComponent<NavMeshAgent>();
        ConfigureNavAgent();
        RefreshCache();
        ResetCombatState();
        EnterWander(); // deterministic starting phase
    }

    // NavMesh integration. We use agent.Move() (frame-by-frame motion delta) rather than
    // SetDestination() so pathfinding is never invoked — pathfinding introduces non-determinism
    // across runs that would corrupt the seeded reproducibility benchmark batches depend on.
    // Obstacle avoidance is also disabled for the same reason.
    private void ConfigureNavAgent()
    {
        if (navAgent == null) return;
        navAgent.updateRotation       = false;                              // FaceNearestEnemy owns rotation
        navAgent.autoBraking          = false;
        navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        navAgent.speed                = moveSpeed * dodgeSpeedMultiplier;   // upper bound; we drive distance per-frame
        navAgent.acceleration         = 9999f;                              // effectively instant — Move() is direct
        if (!navAgent.isOnNavMesh)
            navAgent.Warp(transform.position);                              // snap onto mesh in case spawn was off
    }

    private void Update()
    {
        TickTimers();
        HandlePhaseTransition();

        if (isDodging)
        {
            ExecuteDodge();
            return;
        }

        FaceNearestEnemy();
        ExecutePhase();
        TryAttack();
        ClampToBounds();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Timer ticks
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshCache()
    {
        cachedBoids    = Object.FindObjectsByType<BoidAgent>(FindObjectsSortMode.None);
        cachedManagers = Object.FindObjectsByType<FlockManager>(FindObjectsSortMode.None);
        cacheRefreshTimer = 0.5f;
    }

    private void TickTimers()
    {
        cacheRefreshTimer -= Time.deltaTime;
        if (cacheRefreshTimer <= 0f)
            RefreshCache();

        phaseTimer        -= Time.deltaTime;
        lightAttackTimer  =  Mathf.Max(lightAttackTimer  - Time.deltaTime, 0f);
        heavyAttackTimer  =  Mathf.Max(heavyAttackTimer  - Time.deltaTime, 0f);
        aoeBurstTimer     =  Mathf.Max(aoeBurstTimer     - Time.deltaTime, 0f);
        rangedThrowTimer  =  Mathf.Max(rangedThrowTimer  - Time.deltaTime, 0f);
        dodgeCooldownTimer=  Mathf.Max(dodgeCooldownTimer- Time.deltaTime, 0f);

        if (comboCount > 0)
        {
            comboWindowTimer -= Time.deltaTime;
            if (comboWindowTimer <= 0f)
                comboCount = 0;
        }

        if (heavyWindingUp)
        {
            heavyWindTimer -= Time.deltaTime;
            if (heavyWindTimer <= 0f)
            {
                ReleaseHeavyAttack();
                heavyWindingUp = false;
            }
        }

        if (isDodging)
        {
            dodgeTimer -= Time.deltaTime;
            if (dodgeTimer <= 0f)
            {
                isDodging = false;
                if (playerHealth != null)
                    playerHealth.IsInvincible = false;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phase management
    // ─────────────────────────────────────────────────────────────────────────

    private void HandlePhaseTransition()
    {
        if (phaseTimer > 0f) return;

        // Health-reactive retreat check (uses no Random — deterministic)
        if (playerHealth != null && playerHealth.HealthPercent < retreatHealthThreshold)
        {
            EnterRetreat();
            return;
        }

        PickNewPhase();
    }

    // All Random calls live in PickNewPhase + Enter* methods so the seed controls
    // the full sequence without any per-frame randomness.
    private void PickNewPhase()
    {
        float roll = Random.value;
        float sum  = 0f;

        sum += wanderWeight;        if (roll < sum) { EnterWander();        return; }
        sum += approachWeight;      if (roll < sum) { EnterApproach();      return; }
        sum += circleStrafeWeight;  if (roll < sum) { EnterCircleStrafe();  return; }
        sum += hitAndRunWeight;     if (roll < sum) { EnterHitAndRun();     return; }
        sum += standAndFightWeight; if (roll < sum) { EnterStandAndFight(); return; }
        EnterKite();
    }

    private void EnterWander()
    {
        currentPhase  = PlayerPhase.Wander;
        phaseTimer    = Random.Range(2f, 4f);
        moveDirection = RandomHorizontalDir();
        LogPhase("Wander");
    }

    private void EnterApproach()
    {
        currentPhase = PlayerPhase.Approach;
        phaseTimer   = Random.Range(3f, 6f);
        LogPhase("Approach");
    }

    private void EnterCircleStrafe()
    {
        currentPhase = PlayerPhase.CircleStrafe;
        phaseTimer   = Random.Range(3f, 6f);
        orbitAngle   = Random.Range(0f, 360f);
        LogPhase("CircleStrafe");
    }

    private void EnterHitAndRun()
    {
        currentPhase = PlayerPhase.HitAndRun;
        phaseTimer   = Random.Range(2f, 4f);
        LogPhase("HitAndRun");
    }

    private void EnterStandAndFight()
    {
        currentPhase = PlayerPhase.StandAndFight;
        phaseTimer   = Random.Range(2f, 5f);
        LogPhase("StandAndFight");
    }

    private void EnterKite()
    {
        currentPhase = PlayerPhase.Kite;
        phaseTimer   = Random.Range(3f, 6f);
        orbitAngle   = Random.Range(0f, 360f);
        LogPhase("Kite");
    }

    private void EnterRetreat()
    {
        currentPhase  = PlayerPhase.Retreat;
        phaseTimer    = retreatDuration;
        moveDirection = GetRetreatDirection();
        BehavioralMetricsCollector.Instance?.LogEvent(
            "PlayerRetreat", gameObject.name, -1,
            $"HP={playerHealth?.HealthPercent:F2}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phase execution
    // ─────────────────────────────────────────────────────────────────────────

    private void ExecutePhase()
    {
        switch (currentPhase)
        {
            case PlayerPhase.Wander:
                MoveInDirection(moveDirection);
                break;

            case PlayerPhase.Approach:
                Vector3 flockPos = GetNearestFlockCenter();
                if (flockPos != Vector3.zero)
                {
                    moveDirection = Flat(flockPos - transform.position).normalized;
                    MoveInDirection(moveDirection);
                }
                break;

            case PlayerPhase.CircleStrafe:
                ExecuteOrbit(orbitRadius);
                break;

            case PlayerPhase.HitAndRun:
                ExecuteHitAndRun();
                break;

            case PlayerPhase.StandAndFight:
                // Stay put — all work done in TryAttack
                break;

            case PlayerPhase.Kite:
                ExecuteKite();
                break;

            case PlayerPhase.Retreat:
                MoveInDirection(moveDirection);
                break;
        }
    }

    private void ExecuteOrbit(float radius)
    {
        Vector3 center = GetNearestFlockCenter();
        if (center == Vector3.zero) return;

        orbitAngle += orbitSpeed * Time.deltaTime;
        float rad = orbitAngle * Mathf.Deg2Rad;
        Vector3 orbitTarget = center + new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
        orbitTarget.y = transform.position.y;

        moveDirection = Flat(orbitTarget - transform.position).normalized;
        MoveInDirection(moveDirection);
    }

    private void ExecuteHitAndRun()
    {
        Vector3 center = GetNearestFlockCenter();
        if (center == Vector3.zero) return;

        float dist = Vector3.Distance(transform.position, center);

        if (dist > lightAttackRange * 0.75f)
        {
            // Close in
            moveDirection = Flat(center - transform.position).normalized;
            MoveInDirection(moveDirection);
        }
        else if (lightAttackTimer <= 0f)
        {
            // In range — execute attack then dodge away
            TryLightAttack();
            TryDodge(Flat(transform.position - center).normalized);
        }
    }

    private void ExecuteKite()
    {
        Vector3 center = GetNearestFlockCenter();
        if (center == Vector3.zero) return;

        float dist = Vector3.Distance(transform.position, center);

        if (dist < kiteRange - 2f)
        {
            // Too close — back off
            moveDirection = Flat(transform.position - center).normalized;
            MoveInDirection(moveDirection);
        }
        else if (dist > kiteRange + 2f)
        {
            // Too far — close slightly
            moveDirection = Flat(center - transform.position).normalized;
            MoveInDirection(moveDirection);
        }
        else
        {
            // Sweet spot — orbit while firing
            ExecuteOrbit(kiteRange);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Combat
    // ─────────────────────────────────────────────────────────────────────────

    private void TryAttack()
    {
        if (heavyWindingUp) return; // committed to wind-up

        // AOE burst: highest priority when surrounded
        if (aoeBurstTimer <= 0f && HasEnemiesInRange(aoeBurstRange))
        {
            ExecuteAOEBurst();
            return;
        }

        // Heavy finisher: triggers after a full combo
        if (comboCount >= maxComboCount && heavyAttackTimer <= 0f)
        {
            StartHeavyWindUp();
            return;
        }

        // Ranged: while kiting or when nothing is in melee range
        if (rangedThrowTimer <= 0f
            && (currentPhase == PlayerPhase.Kite || !HasEnemiesInRange(lightAttackRange)))
        {
            TryRangedThrow();
            return;
        }

        // Light attack: the base combo builder
        if (lightAttackTimer <= 0f && HasEnemiesInRange(lightAttackRange))
            TryLightAttack();
    }

    private void TryLightAttack()
    {
        if (lightAttackTimer > 0f) return;

        var enemies = GetEnemiesInRange(lightAttackRange);
        // Cone filter first, then closest-first cap.
        var inCone = new List<IEnemy>(enemies.Count);
        foreach (var e in enemies)
            if (InCone(GetEnemyPosition(e), lightAttackConeHalfAngle))
                inCone.Add(e);
        TakeClosest(inCone, lightAttackMaxTargets);

        int hits = 0;
        foreach (var e in inCone)
        {
            e.TakeDamage(lightAttackDamage);
            hits++;
        }

        lightAttackTimer  = lightAttackCooldown;
        comboCount++;
        comboWindowTimer  = comboWindowDuration;

        BehavioralMetricsCollector.Instance?.LogEvent(
            "PlayerLightAttack", gameObject.name, -1,
            $"Hits={hits},Combo={comboCount},Dmg={lightAttackDamage}");
    }

    private void StartHeavyWindUp()
    {
        heavyWindingUp = true;
        heavyWindTimer = heavyWindUpDuration;
        heavyAttackTimer = heavyAttackCooldown;

        BehavioralMetricsCollector.Instance?.LogEvent(
            "PlayerHeavyWindUp", gameObject.name, -1, $"Combo={comboCount}");
    }

    private void ReleaseHeavyAttack()
    {
        var enemies = GetEnemiesInRange(heavyAttackRange);
        TakeClosest(enemies, heavyAttackMaxTargets);
        int hits = 0;
        foreach (var e in enemies)
        {
            e.TakeDamage(heavyAttackDamage);
            hits++;
        }

        comboCount = 0;

        BehavioralMetricsCollector.Instance?.LogEvent(
            "PlayerHeavyAttack", gameObject.name, -1,
            $"Hits={hits},Dmg={heavyAttackDamage}");
    }

    private void ExecuteAOEBurst()
    {
        var enemies = GetEnemiesInRange(aoeBurstRange);
        TakeClosest(enemies, aoeBurstMaxTargets);
        int hits = 0;
        foreach (var e in enemies)
        {
            e.TakeDamage(aoeBurstDamage);
            hits++;
        }

        aoeBurstTimer = aoeBurstCooldown;
        comboCount    = 0;

        BehavioralMetricsCollector.Instance?.LogEvent(
            "PlayerAOEBurst", gameObject.name, -1,
            $"Hits={hits},Range={aoeBurstRange},Dmg={aoeBurstDamage}");
    }

    /// <summary>
    /// In-place: keep only the <paramref name="maxTargets"/> enemies closest to
    /// <paramref name="origin"/>. Models the physical reach of a single swing/
    /// swing-arc — without this, dense clusters absorb the full strike and a
    /// tightly-cohered flock drains its pooled HP super-linearly with N.
    /// <paramref name="maxTargets"/> &lt;= 0 disables (keeps all).
    ///
    /// Public-static so EditMode tests (`AutomatedPlayerLogicTests.TakeClosest_*`)
    /// can drive it without instantiating an AutomatedPlayer GameObject.
    /// </summary>
    public static void TakeClosest(List<IEnemy> enemies, Vector3 origin,
                                   Func<IEnemy, Vector3> getPos, int maxTargets)
    {
        if (maxTargets <= 0 || enemies == null || enemies.Count <= maxTargets) return;

        enemies.Sort((a, b) =>
        {
            float da = (getPos(a) - origin).sqrMagnitude;
            float db = (getPos(b) - origin).sqrMagnitude;
            return da.CompareTo(db);
        });
        enemies.RemoveRange(maxTargets, enemies.Count - maxTargets);
    }

    /// <summary>Instance shorthand: caps using this player's position and the
    /// concrete-type position resolver.</summary>
    private void TakeClosest(List<IEnemy> enemies, int maxTargets)
    {
        TakeClosest(enemies, transform.position, GetEnemyPosition, maxTargets);
    }

    private void TryRangedThrow()
    {
        IEnemy target = GetNearestEnemy(rangedThrowRange);
        if (target == null) return;

        target.TakeDamage(rangedThrowDamage);
        rangedThrowTimer = rangedThrowCooldown;

        BehavioralMetricsCollector.Instance?.LogEvent(
            "PlayerRangedThrow", gameObject.name, -1, $"Dmg={rangedThrowDamage}");
    }

    private void TryDodge(Vector3 direction)
    {
        if (isDodging || dodgeCooldownTimer > 0f) return;

        isDodging     = true;
        dodgeTimer    = dodgeDuration;
        dodgeCooldownTimer = dodgeCooldown;
        dodgeDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : moveDirection;

        if (playerHealth != null)
            playerHealth.IsInvincible = true;

        BehavioralMetricsCollector.Instance?.LogEvent(
            "PlayerDodge", gameObject.name, -1,
            $"Phase={currentPhase}");
    }

    private void ExecuteDodge()
    {
        Vector3 motion = dodgeDirection * moveSpeed * dodgeSpeedMultiplier * Time.deltaTime;
        if (navAgent != null && navAgent.isOnNavMesh)
            navAgent.Move(motion);
        else
            transform.position += motion;
        ClampToBounds();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Movement helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void FaceNearestEnemy()
    {
        IEnemy nearest = GetNearestEnemy(50f);
        if (nearest == null) return;

        Vector3 dir = Flat(GetEnemyPosition(nearest) - transform.position);
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion target = Quaternion.LookRotation(dir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, rotationSpeed * Time.deltaTime);
        }
    }

    private void MoveInDirection(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.001f) return;
        Vector3 motion = dir.normalized * moveSpeed * Time.deltaTime;
        if (navAgent != null && navAgent.isOnNavMesh)
            navAgent.Move(motion);
        else
            transform.position += motion;
    }

    private void ClampToBounds()
    {
        if (navAgent != null && navAgent.isOnNavMesh) return; // NavMesh keeps us on the mesh; RoomBounds is redundant
        if (roomBounds == null) return;

        Vector3 pos     = transform.position;
        Vector3 clamped = roomBounds.ClampToRoom(pos);

        if (Vector3.Distance(pos, clamped) > 0.1f)
        {
            transform.position = clamped;
            moveDirection = Flat(roomBounds.transform.position - clamped).normalized;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Enemy detection — works across all 4 conditions
    // ─────────────────────────────────────────────────────────────────────────

    private bool HasEnemiesInRange(float range)
    {
        // Physics-based (works for any condition as long as enemies have colliders)
        if (enemyLayerMask != 0)
        {
            return Physics.CheckSphere(transform.position, range, enemyLayerMask);
        }

        // Fallback: check static registries for conditions without layer setup
        float rangeSq = range * range;
        foreach (var a in GOAPBoidAgent.AllAgents)
            if (!a.IsDead && (a.Position - transform.position).sqrMagnitude <= rangeSq) return true;
        foreach (var a in PureGOAPAgent.AllAgents)
            if (!a.IsDead && (a.Position - transform.position).sqrMagnitude <= rangeSq) return true;

        // BoidAgent-based conditions (PureBOIDS / Leader): check via cached FlockManagers
        foreach (var mgr in cachedManagers)
        {
            if (mgr == null || mgr.BoidCount == 0) continue;
            if ((mgr.GetFlockCenter() - transform.position).sqrMagnitude <= rangeSq) return true;
        }
        return false;
    }

    private List<IEnemy> GetEnemiesInRange(float range)
    {
        var result = new List<IEnemy>();

        if (enemyLayerMask != 0)
        {
            var cols = Physics.OverlapSphere(transform.position, range, enemyLayerMask);
            foreach (var col in cols)
            {
                var e = col.GetComponent<IEnemy>();
                if (e != null && !e.IsDead)
                    result.Add(e);
            }
            return result;
        }

        float rangeSq = range * range;
        foreach (var a in GOAPBoidAgent.AllAgents)
            if (!a.IsDead && (a.Position - transform.position).sqrMagnitude <= rangeSq) result.Add(a);
        foreach (var a in PureGOAPAgent.AllAgents)
            if (!a.IsDead && (a.Position - transform.position).sqrMagnitude <= rangeSq) result.Add(a);

        // For BoidAgent conditions, use cached boid array
        foreach (var b in cachedBoids)
            if (b != null && !b.IsDead && (b.Position - transform.position).sqrMagnitude <= rangeSq) result.Add(b);

        return result;
    }

    private IEnemy GetNearestEnemy(float maxRange)
    {
        float bestSq = maxRange * maxRange;
        IEnemy best  = null;

        foreach (var a in GOAPBoidAgent.AllAgents)
        {
            if (a.IsDead) continue;
            float sq = (a.Position - transform.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = a; }
        }

        foreach (var a in PureGOAPAgent.AllAgents)
        {
            if (a.IsDead) continue;
            float sq = (a.Position - transform.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = a; }
        }

        foreach (var b in cachedBoids)
        {
            if (b == null || b.IsDead) continue;
            float sq = (b.Position - transform.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = b; }
        }

        return best;
    }

    // Returns the world position of an IEnemy regardless of concrete type
    private static Vector3 GetEnemyPosition(IEnemy enemy)
    {
        if (enemy is GOAPBoidAgent g) return g.Position;
        if (enemy is PureGOAPAgent p) return p.Position;
        if (enemy is BoidAgent     b) return b.Position;
        if (enemy is MonoBehaviour m) return m.transform.position;
        return Vector3.zero;
    }

    private Vector3 GetNearestFlockCenter()
    {
        // GOAPWithBOIDSMovement: centroid of active flocks
        if (GOAPBoidAgent.AllAgents.Count > 0)
        {
            Vector3 c0 = GOAPBoidAgent.GetFlockCentroid(0);
            Vector3 c1 = GOAPBoidAgent.GetFlockCentroid(1);
            if (c0 != Vector3.zero && c1 != Vector3.zero) return (c0 + c1) * 0.5f;
            if (c0 != Vector3.zero) return c0;
            if (c1 != Vector3.zero) return c1;
        }

        // PureGOAP: centroid of all individual agents
        if (PureGOAPAgent.AllAgents.Count > 0)
        {
            Vector3 sum = Vector3.zero;
            foreach (var a in PureGOAPAgent.AllAgents) sum += a.Position;
            return sum / PureGOAPAgent.AllAgents.Count;
        }

        // PureBOIDS / BOIDSWithGOAPLeader: use cached FlockManager centers
        float   bestDist = float.MaxValue;
        Vector3 best     = Vector3.zero;
        foreach (var mgr in cachedManagers)
        {
            if (mgr == null || mgr.BoidCount == 0) continue;
            Vector3 c    = mgr.GetFlockCenter();
            float   dist = Vector3.Distance(transform.position, c);
            if (dist < bestDist) { bestDist = dist; best = c; }
        }
        return best;
    }

    private Vector3 GetRetreatDirection()
    {
        Vector3 flockCenter = GetNearestFlockCenter();
        if (flockCenter != Vector3.zero)
            return Flat(transform.position - flockCenter).normalized;
        return RandomHorizontalDir();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Geometry helpers
    // ─────────────────────────────────────────────────────────────────────────

    private bool InCone(Vector3 targetPos, float halfAngle)
    {
        Vector3 dir = Flat(targetPos - transform.position);
        if (dir.sqrMagnitude < 0.001f) return true; // point blank — always hits
        return Vector3.Angle(transform.forward, dir.normalized) <= halfAngle;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    // Random.Range call lives here — part of the seeded sequence
    private static Vector3 RandomHorizontalDir()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    private void ResetCombatState()
    {
        lightAttackTimer   = 0f;
        heavyAttackTimer   = 0f;
        aoeBurstTimer      = aoeBurstCooldown; // prevent instant AOE on first encounter
        rangedThrowTimer   = 0f;
        dodgeCooldownTimer = 0f;
        comboCount         = 0;
        comboWindowTimer   = 0f;
        heavyWindingUp     = false;
        isDodging          = false;
    }

    private void LogPhase(string phase)
    {
        BehavioralMetricsCollector.Instance?.LogEvent(
            "PlayerPhase", gameObject.name, -1, phase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Debug gizmos
    // ─────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!drawDebug) return;

        // Light attack range
        Gizmos.color = new Color(1f, 0.8f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, lightAttackRange);

        // AOE burst range
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, aoeBurstRange);

        // Ranged throw range
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, rangedThrowRange);

        // Orbit radius
        Gizmos.color = new Color(0.5f, 1f, 0.5f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, orbitRadius);

        // Velocity arrow
        Gizmos.color = Color.white;
        Gizmos.DrawLine(transform.position, transform.position + moveDirection * 3f);
    }
}
