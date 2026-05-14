/// <summary>
/// Pure-function goal priority resolution for both GOAP brain types.
/// Extracted from brain Update() logic to enable edit-mode unit testing.
/// </summary>
public static class GoalPriorityResolver
{
    public enum GoalType
    {
        Scatter,
        Flee,
        Attack,
        RangedAttack,
        Kite,
        Regroup,
        Flank,
        Guard,
        Wander
    }

    /// <summary>
    /// Resolves which goal should be active for a GOAPWithBOIDSMovement agent.
    /// Priority: Scatter > Flee > Attack/Kite > Regroup > Flank > Guard > Wander
    ///
    /// Hysteresis (added 2026-05-06): when <paramref name="previousGoal"/> is Flee or
    /// Scatter, the corresponding health threshold is treated as
    /// <c>threshold + hysteresisBuffer</c> for the *exit* check. Prevents the
    /// Flee↔Regroup oscillation observed in the 2026-05-06 smoke batch when
    /// healthPercent hovers near fleeHealthThreshold and the resolver flips between
    /// "below threshold → Flee (highest priority that fires)" and "at threshold →
    /// fall through to Regroup (because isIsolated is true after fleeing)" frame to
    /// frame. Defaults to <c>Wander</c> + zero buffer so existing callers (and tests)
    /// that don't pass these get the unchanged pre-2026-05-06 behavior.
    /// </summary>
    public static GoalType ResolveGOAPBoidGoal(
        float healthPercent,
        bool playerNearby,
        bool cooldownReady,
        bool isRanged,
        float playerDist,
        bool isIsolated,
        int flockCount,
        bool attackSlotAvailable = true,
        float criticalHealthThreshold = 0.15f,
        float kiteMinDistance = 8f,
        float guardInnerRange = 15f,
        float guardOuterRange = 30f,
        // fleeHealthThreshold is now explicit so GOAP conditions read the same
        // BoidSettings value as PureBOIDS's FlockStateResolver. Previously
        // hardcoded to 0.3 here, making Inspector tuning of fleeHealthThreshold
        // only affect PureBOIDS — a silent thesis-comparison parity bug.
        float fleeHealthThreshold = 0.3f,
        GoalType previousGoal = GoalType.Wander,
        float hysteresisBuffer = 0f)
    {
        // Sticky Scatter: if we're already scattering, stay scattered until health
        // recovers past the buffer. Otherwise the criticalHealthThreshold boundary
        // lets Scatter→Flee→Scatter thrash the same way Flee→Regroup did.
        if (previousGoal == GoalType.Scatter && playerNearby
            && healthPercent < criticalHealthThreshold + hysteresisBuffer)
            return GoalType.Scatter;

        // Sticky Flee: regardless of playerNearby (flock should stay defensive even
        // if the player briefly leaves perception range while we're at low health).
        if (previousGoal == GoalType.Flee
            && healthPercent < fleeHealthThreshold + hysteresisBuffer)
            return GoalType.Flee;

        if (playerNearby && healthPercent < criticalHealthThreshold)
            return GoalType.Scatter;

        if (playerNearby && healthPercent < fleeHealthThreshold)
            return GoalType.Flee;

        if (playerNearby && cooldownReady && attackSlotAvailable)
        {
            if (isRanged && playerDist < kiteMinDistance)
                return GoalType.Kite;
            if (isRanged)
                return GoalType.RangedAttack;
            return GoalType.Attack;
        }

        if (isIsolated)
            return GoalType.Regroup;

        // No attack slot or cooldown not ready — flank instead
        if (playerNearby && flockCount >= 3)
            return GoalType.Flank;

        if (playerDist >= guardInnerRange && playerDist <= guardOuterRange)
            return GoalType.Guard;

        return GoalType.Wander;
    }

    /// <summary>
    /// Resolves which goal should be active for a BOIDSWithGOAPLeader leader.
    /// Same priority chain but reads thresholds from BoidSettings.
    /// </summary>
    public static GoalType ResolveLeaderGoal(
        float healthPercent,
        bool playerNearby,
        bool cooldownReady,
        bool isRanged,
        float playerDist,
        bool isIsolated,
        int flockCount,
        float criticalHealthThreshold = 0.15f,
        float kiteMinDistance = 8f,
        float guardInnerRange = 15f,
        float guardOuterRange = 30f,
        float fleeHealthThreshold = 0.3f,
        GoalType previousGoal = GoalType.Wander,
        float hysteresisBuffer = 0f)
    {
        // Same priority logic — leader always has an attack slot
        return ResolveGOAPBoidGoal(
            healthPercent, playerNearby, cooldownReady, isRanged,
            playerDist, isIsolated, flockCount,
            attackSlotAvailable: true,
            criticalHealthThreshold, kiteMinDistance, guardInnerRange, guardOuterRange,
            fleeHealthThreshold,
            previousGoal, hysteresisBuffer);
    }
}
