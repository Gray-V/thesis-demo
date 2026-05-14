/// <summary>
/// Pure-function resolver for PureBOIDS flock-level state transitions.
/// Mirrors <see cref="GoalPriorityResolver.ResolveGOAPBoidGoal"/> priority so the same
/// stimulus that fires a GOAP goal in conditions 3/4 fires the analogous flock state
/// in condition 2 — the thesis comparison variable is the decision mechanism, not
/// which behaviors are available.
///
/// Priority: Scattering > Fleeing > Kiting (ranged only) > Engaging > Regrouping >
/// Flanking > Guarding > Grouping > Idle
/// </summary>
public static class FlockStateResolver
{
    public enum FlockBehavior
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

    public static FlockBehavior Resolve(
        float healthPercent,
        bool hasTarget,
        bool playerNearby,
        float playerDist,
        bool isRanged,
        bool attackSlotsSaturated,
        bool meleeAttackActive,
        bool rangedAttackActive,
        float maxDistanceFromCentroid,
        int boidCount,
        bool clusteredEnoughToEngage,
        float criticalHealthThreshold = 0.15f,
        float fleeHealthThreshold = 0.3f,
        float kiteMinDistance = 8f,
        float isolationThreshold = 20f,
        float guardInnerRange = 15f,
        float guardOuterRange = 30f,
        bool flockCooldownActive = false)
    {
        // 1. Scattering — critical HP panic overrides everything
        if (hasTarget && playerNearby && healthPercent < criticalHealthThreshold)
            return FlockBehavior.Scattering;

        // 2. Fleeing — low HP retreat
        if (hasTarget && playerNearby && healthPercent < fleeHealthThreshold)
            return FlockBehavior.Fleeing;

        // 3. Kiting — ranged flock with player too close
        if (hasTarget && playerNearby && isRanged && playerDist < kiteMinDistance)
            return FlockBehavior.Kiting;

        // 4. Flanking — the flock WOULD engage but can't right now, so reposition.
        //    Mirrors GoalPriorityResolver: GOAP flanks when `playerNearby && !cooldownReady`
        //    or `!attackSlotAvailable` and `flockCount >= 3`. PureBOIDS's analogues are
        //    `flockCooldownActive` (melee/ranged flock timer > 0), `attackSlotsSaturated`
        //    (individual WindUp slot count at cap), or an already-active wave/volley
        //    (mele/rangedAttackActive). The boidCount >= 3 gate matches GOAP's flockCount
        //    gate, so the proactive flank only fires when there are enough agents for a
        //    pincer. This replaces the previous reactive-only rule that required an
        //    attack wave to already be active — that gap made PureBOIDS skip Flank when
        //    the flock was on cooldown but idle, violating cross-condition behavioral
        //    parity (Condition 2 vs Conditions 3/4).
        if (hasTarget && playerNearby && boidCount >= 3
            && (attackSlotsSaturated || meleeAttackActive || rangedAttackActive || flockCooldownActive))
            return FlockBehavior.Flanking;

        // 5. Engaging — attack range, healthy, clustered, slots free
        if (hasTarget && playerNearby && clusteredEnoughToEngage)
            return FlockBehavior.Engaging;

        // 6. Regrouping — flock too spread, no immediate combat
        if (!playerNearby && boidCount > 1 && maxDistanceFromCentroid > isolationThreshold)
            return FlockBehavior.Regrouping;

        // 7. Guarding — player at mid-range, no active combat
        if (hasTarget && playerDist >= guardInnerRange && playerDist <= guardOuterRange)
            return FlockBehavior.Guarding;

        // 8. Grouping — target known, still converging
        if (hasTarget && !clusteredEnoughToEngage)
            return FlockBehavior.Grouping;

        // 9. Idle — no target, drift
        return FlockBehavior.Idle;
    }
}
