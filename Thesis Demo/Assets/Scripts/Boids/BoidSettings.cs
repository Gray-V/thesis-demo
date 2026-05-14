using UnityEngine;

public enum FlockType { Melee, Ranged }

[CreateAssetMenu(fileName = "BoidSettings", menuName = "Boids/Boid Settings")]
public class BoidSettings : ScriptableObject
{
    [Header("Flock Identity")]
    public FlockType flockType;
    public Color flockColor = Color.white;

    [Header("Movement")]
    public float minSpeed = 2f;
    public float maxSpeed = 5f;
    public float maxSteerForce = 3f;

    [Header("Detection")]
    public float perceptionRadius = 2.5f;
    public float avoidanceRadius = 1f;

    [Header("Rule Weights")]
    public float separationWeight = 1.5f;
    public float alignmentWeight = 1f;
    public float cohesionWeight = 1f;
    public float crossFlockSeparationWeight = 2f;

    [Header("Obstacle Avoidance")]
    public float obstacleAvoidanceWeight = 10f;
    public float obstacleAvoidanceRadius = 1.5f;
    public LayerMask obstacleMask;

    [Header("Boundary")]
    public float boundaryRadius = 25f;
    public float boundaryTurnStrength = 5f;

    [Header("Spawning")]
    public int flockSize = 40;
    public float spawnRadius = 5f;

    [Header("Target Following")]
    public float targetFollowSpeed = 3f;
    public float targetStopDistance = 2f;
    public float targetSlowDistance = 8f;
    public float aggroRadius = 15f;
    public string aggroTag = "Player";

    [Header("Engagement")]
    public float targetSeekWeight = 0f;
    public float engageBoundaryRadius = -1f;
    public float engageAvoidanceRadius = -1f;
    public float targetKeepDistance = 5f;
    public float groupUpThreshold = 0.7f;

    [Header("Combat")]
    public float maxHealth = 100f;
    public float attackDamage = 10f;
    public float attackCooldown = 1.5f;
    [Tooltip("Fraction of boids kept alive as a last stand until flock health hits 0")]
    public float minSurvivorFraction = 0.35f;

    [Header("Parity Behaviors (Flee / Scatter / Flank)")]
    [Tooltip("Health fraction below which the flock enters Fleeing state (above criticalHealthThreshold)")]
    public float fleeHealthThreshold = 0.3f;
    [Tooltip("Speed multiplier during Fleeing state")]
    public float fleeSpeedMultiplier = 1.3f;
    [Tooltip("How long Scattering lasts before reverting to Fleeing/Engaging (seconds)")]
    public float scatterDuration = 3f;
    [Tooltip("Multiplier applied to cohesionWeight during Regrouping state")]
    public float regroupCohesionMultiplier = 3f;
    [Tooltip("Angular offset (degrees) applied to target-seek direction during Flanking")]
    public float flankAngleDegrees = 60f;

    [Header("Ranged Attack (Flock Coordinated)")]
    public float flockAttackTriggerDistance = 12f;
    public float formationRingRadius = 5f;
    public float formationDuration = 2.0f;
    public float formationConvergeThreshold = 0.7f;
    public float formationSlotTolerance = 1.5f;
    public float formationApproachSpeed = 8f;
    public float flockAttackCooldown = 5.0f;
    public float flockProjectileDamage = 30f;
    public float projectileSpeed = 15f;
    public GameObject projectilePrefab;
    public float projectileTurnSpeed = 5f;
    public float formationOrbitSpeed = 90f;
    public float formationKnockbackForce = 8f;

    [Header("Melee Attack Behaviour")]
    [Tooltip("Distance at which a boid triggers its wind-up")]
    public float attackTriggerDistance = 4f;
    [Tooltip("How long the boid pulls back before charging (seconds)")]
    public float attackWindUpDuration = 0.4f;
    [Tooltip("(Unused for melee — wind-up is now a hover)")]
    public float attackWindUpDistance = 2f;
    [Tooltip("How long the charge dash lasts (seconds)")]
    public float attackChargeDuration = 0.25f;
    [Tooltip("(Unused for melee — speed is arc-duration-based)")]
    public float attackChargeSpeed = 20f;
    [Tooltip("Duration of the full infinity-arc sweep (seconds)")]
    public float attackSweepDuration = 0.8f;
    [Tooltip("Distance at which the charge deals damage")]
    public float attackContactDistance = 1.5f;
    [Tooltip("Max boids from this flock that can be in WindUp or Charging at once")]
    public int maxSimultaneousAttackers = 3;

    [Header("Melee Attack (Flock Coordinated)")]
    public float meleeFlockTriggerDistance = 8f;
    public float meleeFlockCooldown = 4f;
    public float meleeRecoveryDuration = 0.5f;
    public float meleeWindUpSteerWeight = 5f;    // how strongly boids steer away during wind-up
    public float meleeChargeSteerWeight = 10f;   // how strongly boids steer toward charge target

    [Header("Tactical Behaviors")]
    [Tooltip("Distance from flock centroid that triggers Regroup")]
    public float isolationThreshold = 20f;
    [Tooltip("Distance from centroid at which Regroup completes")]
    public float regroupRadius = 8f;
    [Tooltip("Health fraction below which Scatter triggers (below Flee threshold)")]
    public float criticalHealthThreshold = 0.15f;
    [Tooltip("Speed multiplier during Scatter")]
    public float scatterSpeedMultiplier = 1.5f;
    [Tooltip("Min distance from player before ranged agents start kiting")]
    public float kiteMinDistance = 8f;
    [Tooltip("Max distance at which ranged agents fire while kiting")]
    public float kiteMaxDistance = 15f;
    [Tooltip("Inner range of Guard zone (closer triggers Attack)")]
    public float guardInnerRange = 15f;
    [Tooltip("Outer range of Guard zone (farther triggers Wander)")]
    public float guardOuterRange = 30f;
}
