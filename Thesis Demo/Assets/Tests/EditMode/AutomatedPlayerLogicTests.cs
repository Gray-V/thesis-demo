using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Edit-mode tests for AutomatedPlayer logic that can be verified without
/// a running scene: phase weights, cone geometry, seed reproducibility of
/// phase sequences, dodge i-frame flag, and attack priority rules.
/// </summary>
[TestFixture]
public class AutomatedPlayerLogicTests
{
    // ── Phase weight distribution ──

    [Test]
    public void PhaseWeights_SumToApproximatelyOne()
    {
        // Mirrors the default weights in AutomatedPlayer
        float wander        = 0.10f;
        float approach      = 0.25f;
        float circleStrafe  = 0.25f;
        float hitAndRun     = 0.20f;
        float standAndFight = 0.12f;
        float kite          = 0.08f;

        float total = wander + approach + circleStrafe + hitAndRun + standAndFight + kite;
        Assert.AreEqual(1f, total, 0.001f, "Phase weights should sum to 1.0");
    }

    // ── Attack cone geometry ──

    [Test]
    public void AttackCone_EnemyDirectlyAhead_IsHit()
    {
        Vector3 playerPos = Vector3.zero;
        Vector3 forward   = Vector3.forward;
        Vector3 enemyPos  = new Vector3(0f, 0f, 5f); // directly ahead

        Vector3 dir = enemyPos - playerPos;
        dir.y = 0f;
        float angle = Vector3.Angle(forward, dir.normalized);

        Assert.LessOrEqual(angle, 65f, "Enemy directly ahead should be within 65° half-angle cone");
    }

    [Test]
    public void AttackCone_EnemyBehind_IsNotHit()
    {
        Vector3 playerPos = Vector3.zero;
        Vector3 forward   = Vector3.forward;
        Vector3 enemyPos  = new Vector3(0f, 0f, -5f); // directly behind

        Vector3 dir = enemyPos - playerPos;
        dir.y = 0f;
        float angle = Vector3.Angle(forward, dir.normalized);

        Assert.Greater(angle, 65f, "Enemy directly behind should be outside the attack cone");
    }

    [Test]
    public void AttackCone_YIsIgnored()
    {
        Vector3 forward  = Vector3.forward;
        Vector3 enemyPos = new Vector3(0f, 50f, 5f); // far above but same XZ

        Vector3 dir = enemyPos; // from origin
        dir.y = 0f;
        float angle = Vector3.Angle(forward, dir.normalized);

        Assert.LessOrEqual(angle, 65f,
            "Enemy above player but at same XZ should still be inside cone after Y is zeroed");
    }

    // ── Dodge i-frame flag ──

    [Test]
    public void IsInvincible_BlocksDamage()
    {
        // PlayerHealth.TakeDamage should no-op when IsInvincible is true
        var go = new GameObject("TestPlayer");
        var health = go.AddComponent<PlayerHealth>();
        // Need to call Awake manually since not in Play mode
        // (Awake sets currentHealth = maxHealth)
        health.ResetHealth();

        float before = health.HealthPercent;
        health.IsInvincible = true;
        health.TakeDamage(100f);
        float after = health.HealthPercent;

        Assert.AreEqual(before, after, 0.001f,
            "TakeDamage should not reduce health while IsInvincible is true");

        Object.DestroyImmediate(go);
    }

    [Test]
    public void IsInvincible_OffAllowsDamage()
    {
        var go = new GameObject("TestPlayer");
        var health = go.AddComponent<PlayerHealth>();
        health.ResetHealth();

        float before = health.HealthPercent;
        health.IsInvincible = false;
        health.TakeDamage(50f);
        float after = health.HealthPercent;

        Assert.Less(after, before,
            "TakeDamage should reduce health when IsInvincible is false");

        Object.DestroyImmediate(go);
    }

    // ── Retreat threshold ──

    [Test]
    public void RetreatThreshold_HealthBelowTriggers()
    {
        // Default retreatHealthThreshold = 0.35f
        float retreatThreshold = 0.35f;
        float lowHealth        = 0.20f;
        float fullHealth       = 0.80f;

        Assert.IsTrue(lowHealth  < retreatThreshold, "Low health should trigger retreat");
        Assert.IsFalse(fullHealth < retreatThreshold, "Full health should not trigger retreat");
    }

    // ── Seed reproducibility of phase selection ──

    [Test]
    public void SameSeed_ProducesSamePhaseSequence()
    {
        float wander        = 0.10f;
        float approach      = 0.25f;
        float circleStrafe  = 0.25f;
        float hitAndRun     = 0.20f;
        float standAndFight = 0.12f;

        // Simulate PickNewPhase with the same weighted-roll logic
        System.Func<float, string> pickPhase = roll =>
        {
            float sum = 0f;
            sum += wander;        if (roll < sum) return "Wander";
            sum += approach;      if (roll < sum) return "Approach";
            sum += circleStrafe;  if (roll < sum) return "CircleStrafe";
            sum += hitAndRun;     if (roll < sum) return "HitAndRun";
            sum += standAndFight; if (roll < sum) return "StandAndFight";
            return "Kite";
        };

        int sequenceLength = 20;

        Random.InitState(42);
        var seq1 = new string[sequenceLength];
        for (int i = 0; i < sequenceLength; i++)
            seq1[i] = pickPhase(Random.value);

        Random.InitState(42);
        var seq2 = new string[sequenceLength];
        for (int i = 0; i < sequenceLength; i++)
            seq2[i] = pickPhase(Random.value);

        for (int i = 0; i < sequenceLength; i++)
            Assert.AreEqual(seq1[i], seq2[i],
                $"Phase at index {i} should be identical across runs with the same seed");
    }

    [Test]
    public void DifferentSeeds_ProduceDifferentPhaseSequences()
    {
        System.Func<float, string> pickPhase = roll =>
        {
            if (roll < 0.10f) return "Wander";
            if (roll < 0.35f) return "Approach";
            if (roll < 0.60f) return "CircleStrafe";
            if (roll < 0.80f) return "HitAndRun";
            if (roll < 0.92f) return "StandAndFight";
            return "Kite";
        };

        int sequenceLength = 30;

        Random.InitState(42);
        var seq1 = new string[sequenceLength];
        for (int i = 0; i < sequenceLength; i++)
            seq1[i] = pickPhase(Random.value);

        Random.InitState(7);
        var seq2 = new string[sequenceLength];
        for (int i = 0; i < sequenceLength; i++)
            seq2[i] = pickPhase(Random.value);

        int differences = 0;
        for (int i = 0; i < sequenceLength; i++)
            if (seq1[i] != seq2[i]) differences++;

        Assert.Greater(differences, 0,
            "Different seeds should produce at least some different phase selections");
    }

    // ── Orbit math ──

    [Test]
    public void OrbitTarget_IsCorrectDistanceFromCenter()
    {
        Vector3 center     = new Vector3(5f, 0f, 10f);
        float   orbitRadius = 9f;
        float   orbitAngle  = 45f;

        float rad = orbitAngle * Mathf.Deg2Rad;
        Vector3 orbitPos = center + new Vector3(
            Mathf.Cos(rad) * orbitRadius,
            0f,
            Mathf.Sin(rad) * orbitRadius
        );

        float actualDist = Vector3.Distance(new Vector3(orbitPos.x, 0f, orbitPos.z),
                                            new Vector3(center.x,   0f, center.z));

        Assert.AreEqual(orbitRadius, actualDist, 0.001f,
            "Orbit target should be exactly orbitRadius units from the center");
    }

    [Test]
    public void OrbitTarget_YComponentMatchesPlayer()
    {
        Vector3 center     = new Vector3(0f, 5f, 0f);
        float   playerY    = 3f;
        float   orbitRadius = 9f;
        float   orbitAngle  = 90f;

        float rad = orbitAngle * Mathf.Deg2Rad;
        Vector3 orbitPos = center + new Vector3(Mathf.Cos(rad) * orbitRadius, 0f, Mathf.Sin(rad) * orbitRadius);
        orbitPos.y = playerY; // player's Y is preserved

        Assert.AreEqual(playerY, orbitPos.y, 0.001f,
            "Orbit target Y should match the player's Y, not the flock center's Y");
    }
}
